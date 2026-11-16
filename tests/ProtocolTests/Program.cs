using STranslate.Plugin.Translate.OpenAICompetitive.Protocol;
using System.Net;
using System.Text.Json;

var passed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
}
void Equal<T>(T expected, T actual)
{
    if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string Delta(string text, int index = 0) => "data: " + JsonSerializer.Serialize(new
{ choices = new[] { new { index, delta = new { content = text } } } });
string Response(string text) => JsonSerializer.Serialize(new
{ choices = new[] { new { index = 0, message = new { content = text }, finish_reason = "stop" } } });

foreach (var baseUrl in new[] { "https://example.com", "https://example.com/", " https://example.com/v1/ ", "https://example.com/v1" })
{
    Check("chat URL " + baseUrl, () => Equal("https://example.com/v1/chat/completions", ApiEndpoints.Chat(baseUrl)));
    Check("models URL " + baseUrl, () => Equal("https://example.com/v1/models", ApiEndpoints.Models(baseUrl)));
}
foreach (var path in new[] { "/api/v1", "/proxy/v2", "/proxy/v1beta" })
    Check("versioned proxy " + path, () => Equal("https://example.com" + path + "/models", ApiEndpoints.Models("https://example.com" + path + "/")));
Check("API proxy root", () => Equal("https://example.com/api/v1/chat/completions", ApiEndpoints.Chat("https://example.com/api")));
Check("legacy custom endpoint preserved", () => Equal("https://example.com/custom", ApiEndpoints.Chat("https://example.com/custom")));
Check("custom model path must be explicit", () => Throws<ArgumentException>(() => ApiEndpoints.Models("https://example.com/custom#")));
Check("full chat endpoint to models", () => Equal("https://example.com/api/v1/models?x=1", ApiEndpoints.Models("https://example.com/api/v1/chat/completions?x=1")));
Check("models endpoint to chat", () => Equal("https://example.com/v1/chat/completions", ApiEndpoints.Chat("https://example.com/v1/models")));
Check("exact custom endpoint", () => Equal("https://example.com/custom?x=1", ApiEndpoints.Chat(" https://example.com/custom?x=1# ")));
Check("query preserved", () => Equal("https://example.com/v1/chat/completions?key=x%2Fy", ApiEndpoints.Chat("https://example.com/?key=x%2Fy")));
foreach (var bad in new string?[] { null, "", "example.com", "ftp://example.com", "https://user:pass@example.com", "https://example.com/#fragment" })
    Check("invalid URL " + bad, () => Throws<ArgumentException>(() => ApiEndpoints.Chat(bad)));
Check("local no-key service", () => Equal(0, ChatProtocol.Headers("  ").Count));
Check("key trimmed", () => Equal("Bearer abc", ChatProtocol.Headers(" abc ")["Authorization"]));
Check("key header injection", () => Throws<ArgumentException>(() => ChatProtocol.Headers("abc\r\nx: y")));
Check("literal placeholders in original", () => Equal("$source $target $content -> 中文", ChatProtocol.Expand("$content -> $target", "English", "中文", "$source $target $content")));
Check("literal placeholders in replacement", () => Equal("$target $content 原文", ChatProtocol.Expand("$source $target $content", "$target", "$content", "原文")));
Check("model IDs clean and deduplicated", () => Equal("a,z", string.Join(',', ChatProtocol.ReadModels("{\"data\":[{\"id\":\" z \"},null,{}, {\"id\":\"a\"},{\"id\":\"z\"},{\"id\":123}]}"))));
Check("empty model list", () => Throws<InvalidDataException>(() => ChatProtocol.ReadModels("{\"data\":[]}")));
Check("model API error", () => Throws<InvalidOperationException>(() => ChatProtocol.ReadModels("{\"error\":{\"message\":\"unauthorized\"}}")));
Check("malformed models", () => Throws<InvalidDataException>(() => ChatProtocol.ReadModels("{\"data\":{}}")));
var message = new[] { new ChatMessage("user", "Hello") };
Check("request parameters", () =>
{
    var request = ChatProtocol.Request(" model ", message, false, 321, 0.5, 0.8, 2);
    Equal("model", request["model"]); Equal(false, request["stream"]); Equal(321, request["max_tokens"]);
    Equal(0.8, request["top_p"]); Equal(2, request["n"]);
    if (!JsonSerializer.Serialize(request).Contains("\"role\":\"user\"")) throw new Exception("role must be lowercase");
});
Check("request clamps and defaults", () =>
{
    var request = ChatProtocol.Request(null, message, true, 0, double.NaN, 5, 0);
    Equal("gpt-4o", request["model"]); Equal(0.7, request["temperature"]); Equal(1.0, request["top_p"]);
    if (request.ContainsKey("max_tokens")) throw new Exception("0 means provider default");
});
foreach (var model in new[] { "gpt-5", "gpt-5-mini", "gpt-5.1", "o1", "o3-mini", "o4-mini" })
    Check("reasoning parameters " + model, () =>
    {
        var request = ChatProtocol.Request(model, message, true, 100, 0.7, 1, 1);
        Equal(100, request["max_completion_tokens"]);
        if (request.ContainsKey("temperature") || request.ContainsKey("top_p")) throw new Exception("unsupported reasoning parameters");
    });
Check("empty prompt", () => Throws<InvalidOperationException>(() => ChatProtocol.Request("m", [], true, 1, 1, 1, 1)));
Check("SSE comments, metadata, usage and data literal", () =>
{
    var reader = new ChatResponseReader();
    foreach (var line in new[] { ": OPENROUTER PROCESSING", "event: message", "id: 1", "retry: 10", Delta("data: 测试"), "data: {\"choices\":[],\"usage\":{}}", "data: [DONE]" }) reader.ProcessLine(line);
    Equal("data: 测试", reader.Complete());
});
Check("multiple choices do not interleave", () =>
{
    var reader = new ChatResponseReader();
    reader.ProcessLine(Delta("A")); reader.ProcessLine(Delta("B", 1)); reader.ProcessLine("data: [DONE]");
    Equal("A", reader.Complete());
});
Check("reasoning_content omitted", () =>
{
    var reader = new ChatResponseReader();
    reader.ProcessLine("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"secret\"}}]}");
    reader.ProcessLine(Delta("译文")); reader.ProcessLine("data: [DONE]"); Equal("译文", reader.Complete());
});
var tagged = "<think>private reasoning</think>\n\n译文 data: 保留";
Check("think tags at every split boundary", () =>
{
    for (var i = 0; i <= tagged.Length; i++)
    {
        var reader = new ChatResponseReader(); reader.ProcessLine(Delta(tagged[..i])); reader.ProcessLine(Delta(tagged[i..]));
        reader.ProcessLine("data: [DONE]"); Equal("译文 data: 保留", reader.Complete());
    }
});
Check("think tags one character per chunk", () =>
{
    var reader = new ChatResponseReader(); foreach (var c in tagged) reader.ProcessLine(Delta(c.ToString()));
    reader.ProcessLine("data: [DONE]"); Equal("译文 data: 保留", reader.Complete());
});
Check("literal angle brackets preserved", () =>
{
    var reader = new ChatResponseReader(); reader.ProcessLine(Delta("a < b <thi")); reader.ProcessLine("data: [DONE]"); Equal("a < b <thi", reader.Complete());
});
Check("multiline SSE JSON", () =>
{
    var reader = new ChatResponseReader(); reader.ProcessLine("data: {\"choices\": [");
    reader.ProcessLine("data: {\"delta\": {\"content\": \"你好\"}}]}"); reader.ProcessLine("data: [DONE]"); Equal("你好", reader.Complete());
});
Check("nonstream response", () => { var r = new ChatResponseReader(); r.ReadResponse(Response("你好")); Equal("你好", r.Complete()); });
Check("stream provider returns full JSON", () => { var r = new ChatResponseReader(); r.ProcessLine(Response("你好")); Equal("你好", r.Complete()); });
Check("typed text parts", () =>
{
    var r = new ChatResponseReader(); r.ReadResponse("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"你好\"}]}}]}"); Equal("你好", r.Complete());
});
foreach (var json in new[] { "{\"error\":{\"message\":\"bad key\"}}", "{\"choices\":[{\"finish_reason\":\"length\"}]}", "{\"choices\":[{\"finish_reason\":\"content_filter\"}]}", "{\"choices\":[{\"delta\":{\"refusal\":\"no\"}}]}" })
    Check("server rejection " + json, () => Throws<InvalidOperationException>(() => new ChatResponseReader().ProcessLine("data: " + json)));
Check("invalid JSON is not silent", () => { var r = new ChatResponseReader(); r.ProcessLine("data: invalid"); Throws<InvalidDataException>(() => r.Complete()); });
Check("empty response fails", () => { var r = new ChatResponseReader(); r.ProcessLine("data: [DONE]"); Throws<InvalidDataException>(() => r.Complete()); });
Check("truncated stream fails", () => { var r = new ChatResponseReader(); r.ProcessLine(Delta("partial")); Throws<InvalidDataException>(() => r.Complete()); });
Check("unclosed think fails", () => { var r = new ChatResponseReader(); r.ProcessLine(Delta("<think>secret")); r.ProcessLine("data: [DONE]"); Throws<InvalidDataException>(() => r.Complete()); });

async Task RetryCheck(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
}
await RetryCheck("transient HTTP retries", async () =>
{
    var calls = 0;
    var answer = await RetryPolicy.ExecuteAsync(() => ++calls < 3 ? Task.FromException<int>(new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests)) : Task.FromResult(42), () => false, 3, 0, default);
    Equal(42, answer); Equal(3, calls);
});
await RetryCheck("request timeout is retryable", async () =>
{
    var calls = 0;
    var answer = await RetryPolicy.ExecuteAsync(() => ++calls == 1 ? Task.FromException<int>(new TaskCanceledException("timeout")) : Task.FromResult(42), () => false, 1, 0, default);
    Equal(42, answer); Equal(2, calls);
});
await RetryCheck("authentication failure not retried", async () =>
{
    var calls = 0;
    try { await RetryPolicy.ExecuteAsync<int>(() => { calls++; throw new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized); }, () => false, 3, 0, default); throw new Exception("Expected failure"); }
    catch (HttpRequestException) { Equal(1, calls); }
});
await RetryCheck("partial output not retried", async () =>
{
    var calls = 0;
    try { await RetryPolicy.ExecuteAsync<int>(() => { calls++; throw new HttpRequestException("disconnected"); }, () => true, 3, 0, default); throw new Exception("Expected failure"); }
    catch (HttpRequestException) { Equal(1, calls); }
});
await RetryCheck("retry limit", async () =>
{
    var calls = 0;
    try { await RetryPolicy.ExecuteAsync<int>(() => { calls++; throw new HttpRequestException("offline"); }, () => false, 2, 0, default); throw new Exception("Expected failure"); }
    catch (HttpRequestException) { Equal(3, calls); }
});
await RetryCheck("cancellation before request", async () =>
{
    using var cts = new CancellationTokenSource(); cts.Cancel(); var calls = 0;
    try { await RetryPolicy.ExecuteAsync(() => { calls++; return Task.FromResult(1); }, () => false, 3, 0, cts.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Equal(0, calls); }
});
await RetryCheck("cancellation during retry delay", async () =>
{
    using var cts = new CancellationTokenSource(); var calls = 0;
    var pending = RetryPolicy.ExecuteAsync<int>(() => { calls++; throw new HttpRequestException("offline"); }, () => false, 3, 60000, cts.Token);
    cts.Cancel();
    try { await pending; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { Equal(1, calls); }
});
Console.WriteLine($"{passed} protocol tests passed.");

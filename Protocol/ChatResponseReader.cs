using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace STranslate.Plugin.Translate.OpenAICompetitive.Protocol;

public sealed class ChatResponseReader
{
    private readonly StringBuilder _text = new();
    private readonly ThinkingFilter _thinking = new();
    private string _pendingJson = string.Empty;
    private bool _finished;
    private bool _done;
    public string Text => _text.ToString();
    public void ProcessLine(string line)
    {
        foreach (var part in line.Split('\n')) ProcessSingleLine(part.TrimEnd('\r'));
    }
    private void ProcessSingleLine(string line)
    {
        if (_done || string.IsNullOrWhiteSpace(line)) return;
        line = line.TrimStart('\uFEFF');
        if (line.StartsWith(':') || line.StartsWith("event:", StringComparison.Ordinal) ||
            line.StartsWith("id:", StringComparison.Ordinal) || line.StartsWith("retry:", StringComparison.Ordinal)) return;
        if (line.StartsWith("data:", StringComparison.Ordinal)) line = line[5..].TrimStart();
        if (line.Trim() == "[DONE]")
        {
            if (_pendingJson.Length > 0) throw new InvalidDataException("服务端返回了不完整的 JSON 数据。");
            _done = true;
            return;
        }
        var json = _pendingJson.Length == 0 ? line : _pendingJson + "\n" + line;
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException)
        {
            if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("服务端响应不是有效的 JSON 数据。");
            _pendingJson = json;
            return;
        }
        _pendingJson = string.Empty;
        ReadObject(node as JsonObject ?? throw new InvalidDataException("服务端响应不是 JSON 对象。"));
    }
    public void ReadResponse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("服务端响应不是 JSON 对象。");
        ReadObject(root);
        _done = true;
    }
    private void ReadObject(JsonObject root)
    {
        ChatProtocol.ThrowIfError(root);
        if (root["choices"] is not JsonArray choices)
            throw new InvalidDataException("服务端响应缺少 choices 数组。");
        // n > 1 时只展示第一个候选，避免交错拼接多份译文。
        var choice = choices.OfType<JsonObject>().FirstOrDefault(c => c["index"] is null || c["index"]!.ToString() == "0");
        if (choice is null) return;
        var message = (choice["delta"] ?? choice["message"]) as JsonObject;
        if (message?["refusal"] is { } refusal && !string.IsNullOrWhiteSpace(refusal.ToString()))
            throw new InvalidOperationException("模型拒绝了此请求：" + refusal);
        if (message?["content"] is JsonValue value && value.TryGetValue<string>(out var content))
            _text.Append(_thinking.Append(content));
        else if (message?["content"] is JsonArray parts)
            foreach (var part in parts.OfType<JsonObject>())
                if (part["text"] is JsonValue text && text.TryGetValue<string>(out var segment))
                    _text.Append(_thinking.Append(segment));
        var reason = choice["finish_reason"]?.ToString();
        if (reason == "length") throw new InvalidOperationException("输出达到 token 上限，请提高 MaxTokens 后重试。");
        if (reason == "content_filter") throw new InvalidOperationException("服务端内容过滤器阻止了此请求。");
        if (!string.IsNullOrEmpty(reason)) _finished = true;
    }
    public string Complete()
    {
        if (_pendingJson.Length > 0) throw new InvalidDataException("服务端返回了无效或不完整的 JSON 数据。");
        if (!_done && !_finished) throw new InvalidDataException("服务端在响应结束前中断了连接。");
        _text.Append(_thinking.Complete());
        if (string.IsNullOrWhiteSpace(Text)) throw new InvalidDataException("服务端未返回译文，请检查模型、Prompt 及 token 上限。");
        return Text;
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using STranslate.Plugin;
using STranslate.Plugin.Translate.OpenAICompetitive;
using STranslate.Plugin.Translate.OpenAICompetitive.ViewModel;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private static int _passed;
    private static readonly TranslateRequest Request = new("Hi $source", LangEnum.English, LangEnum.ChineseSimplified);
    private const string Response = "{\"choices\":[{\"message\":{\"content\":\"你好\"},\"finish_reason\":\"stop\"}]}";

    [STAThread]
    private static void Main()
    {
        // 在 STA 主线程上实际加载编译后的 XAML，捕获缺失类型和绑定目标问题。
        using (var fixture = new Fixture())
        {
            var view = fixture.Main.GetSettingUI();
            Equal(typeof(SettingsViewModel), view.DataContext.GetType());
            Equal(view, fixture.Main.GetSettingUI());
            Pass("settings XAML loads and is reused");
        }
        RunAsync().GetAwaiter().GetResult();
        Console.WriteLine($"{_passed} plugin integration tests passed.");
    }

    private static async Task RunAsync()
    {
        using (var f = new Fixture())
        {
            foreach (var language in Enum.GetValues<LangEnum>())
                if (f.Main.GetTargetLanguage(language) is null) throw new Exception("Missing language " + language);
            Equal<string?>(null, f.Main.GetTargetLanguage((LangEnum)999));
            var saves = f.Saves; f.Main.SelectPrompt(null); Equal(saves, f.Saves);
            Equal(true, f.Main.Prompts[0].IsEnabled);
            Pass("all SDK languages are mapped and null prompt selection is harmless");
        }
        using (var f = new Fixture(new Settings { ApiKey = null!, Url = null!, Models = null!, Prompts = null!, Model = null! }))
        {
            Equal("https://api.openai.com/", f.Settings.Url);
            Equal("gpt-4o", f.Settings.Model);
            Equal(3, f.Main.Prompts.Count);
            f.Main.Init(f.Context);
            Equal(3, f.Main.Prompts.Count);
            Pass("null settings repaired and Init is idempotent");
        }
        using (var f = new Fixture(new Settings { Stream = false, Model = " custom ", ApiKey = " key ", MaxTokens = 123 }))
        {
            var before = f.Main.Prompts[0].Items[1].Content;
            var result = new TranslateResult();
            await f.Main.TranslateAsync(Request, result);
            Equal(true, result.IsSuccess); Equal("你好", result.Text);
            Equal("PostAsync", f.LastMethod);
            var json = JsonSerializer.Serialize(f.Payload);
            using var data = JsonDocument.Parse(json);
            Equal(false, data.RootElement.GetProperty("stream").GetBoolean());
            Equal(123, data.RootElement.GetProperty("max_tokens").GetInt32());
            Equal("custom", data.RootElement.GetProperty("model").GetString());
            Equal("Bearer key", f.Options!.Headers!["Authorization"]);
            Equal(before, f.Main.Prompts[0].Items[1].Content);
            if (!json.Contains("Hi $source")) throw new Exception("Original text placeholders were changed");
            Pass("nonstream translation uses configured parameters without mutating prompts");
        }
        using (var f = new Fixture())
        {
            f.Lines = ["data: {\"choices\":[{\"delta\":{\"content\":\"<thi\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"content\":\"nk>secret</think>\\n你好 data:\"}}]}", "data: [DONE]"];
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(true, result.IsSuccess); Equal("你好 data:", result.Text);
            Pass("stream translation handles split think tags and literal data prefix");
        }
        using (var f = new Fixture())
        {
            f.Lines = ["data: {\"error\":{\"message\":\"bad model\"}}"];
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(false, result.IsSuccess);
            await ThrowsAsync<InvalidOperationException>(() => f.Main.ValidateApiAsync());
            Pass("server errors fail both translation and validation");
        }
        using (var f = new Fixture())
        {
            f.Lines = ["data: [DONE]"];
            await ThrowsAsync<System.IO.InvalidDataException>(() => f.Main.ValidateApiAsync());
            Pass("validation rejects an empty successful HTTP response");
        }
        using (var f = new Fixture(new Settings { Prompts = [] }))
        {
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(false, result.IsSuccess); Equal(0, f.Requests);
            Equal("NoEnabledPrompt", result.Text);
            Pass("missing prompt fails before making a network request");
        }
        using (var f = new Fixture(new Settings { RetryDelayMilliseconds = 0 }))
        {
            f.BeforeRequest = () => f.Requests < 3 ? new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests) : null;
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(true, result.IsSuccess); Equal(3, f.Requests);
            Pass("translation retries transient failures");
        }
        using (var f = new Fixture(new Settings { RetryDelayMilliseconds = 0 }))
        {
            f.AfterStream = new HttpRequestException("connection lost");
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(false, result.IsSuccess); Equal(1, f.Requests);
            Pass("translation does not replay after emitting partial output");
        }
        using (var f = new Fixture(new Settings { MaxRetries = 0 }))
        {
            f.BeforeRequest = () => new TaskCanceledException("request timed out");
            var result = new TranslateResult(); await f.Main.TranslateAsync(Request, result);
            Equal(false, result.IsSuccess); Equal(1, f.Requests);
            Pass("exhausted request timeout is reported as a failure");
        }
        using (var f = new Fixture())
        {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => f.Main.TranslateAsync(Request, new TranslateResult(), cts.Token));
            Equal(0, f.Requests); Pass("caller cancellation propagates without network calls");
        }
        using (var f = new Fixture())
        using (var vm = new SettingsViewModel(f.Context, f.Settings, f.Main))
        {
            vm.Model = " custom "; vm.AddModel(vm.Model);
            Equal("custom", f.Settings.Model); Equal(true, f.Settings.Models.Contains("custom"));
            vm.DeleteModelCommand.Execute("custom"); Equal(false, f.Settings.Models.Contains("custom"));
            Equal(vm.Model, f.Settings.Model);
            vm.Models.Clear(); Equal(0, f.Settings.Models.Count);
            vm.DisplayName = "  Local provider  "; Equal("Local provider", f.MetaData.Name);
            Pass("manual model edits, deletion, Reset and provider name persist");
        }
        using (var f = new Fixture(new Settings { Model = "manual", ApiKey = "" }))
        using (var vm = new SettingsViewModel(f.Context, f.Settings, f.Main))
        {
            await vm.FetchModelsCommand.ExecuteAsync(null);
            Equal("https://api.openai.com/v1/models", f.Url);
            Equal(true, vm.Models.Contains("manual")); Equal(true, vm.Models.Contains("server-model"));
            Equal("manual", f.Settings.Model); Equal(0, f.Options!.Headers!.Count);
            Pass("model discovery accepts local no-key providers and preserves a manual model");
        }
        using (var f = new Fixture())
        using (var vm = new SettingsViewModel(f.Context, f.Settings, f.Main))
        {
            var gate = new TaskCompletionSource<string>(); f.ModelResponse = gate.Task;
            var pending = vm.FetchModelsCommand.ExecuteAsync(null);
            vm.Url = "https://other.example.com"; gate.SetResult("{\"data\":[{\"id\":\"stale\"}]}");
            await pending; Equal(false, vm.Models.Contains("stale"));
            Pass("outdated model fetch cannot overwrite a changed provider");
        }
        using (var f = new Fixture())
        {
            var vm = new SettingsViewModel(f.Context, f.Settings, f.Main);
            var gate = new TaskCompletionSource<string>(); f.ModelResponse = gate.Task;
            var pending = vm.FetchModelsCommand.ExecuteAsync(null);
            var saved = f.Saves; vm.Dispose(); gate.SetResult("{\"data\":[{\"id\":\"late\"}]}");
            await pending; Equal(saved, f.Saves); Equal(false, f.Settings.Models.Contains("late"));
            Pass("disposed settings cancel in-flight work without saving late results");
        }
    }
    private static void Pass(string name) { _passed++; Console.WriteLine("PASS " + name); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class Fixture : IDisposable
    {
        public Settings Settings { get; }
        public Main Main { get; } = new();
        public PluginMetaData MetaData { get; } = new();
        public IPluginContext Context { get; }
        public int Saves;
        public int Requests;
        public object? Payload;
        public Options? Options;
        public string? Url;
        public string? LastMethod;
        public string[] Lines = ["data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}", "data: [DONE]"];
        public Func<Exception?>? BeforeRequest;
        public Exception? AfterStream;
        public Task<string> ModelResponse = Task.FromResult("{\"data\":[{\"id\":\"server-model\"}]}");

        public Fixture(Settings? settings = null)
        {
            Settings = settings ?? new();
            var http = Stub<IHttpService>.Create((method, args) =>
            {
                Requests++; LastMethod = method.Name; Url = (string)args![0]!;
                if (BeforeRequest?.Invoke() is { } error) return Task.FromException(error);
                if (method.Name == "GetAsync") { Options = (Options?)args[1]; return ModelResponse; }
                Payload = args[1];
                if (method.Name == "PostAsync") { Options = (Options?)args[2]; return Task.FromResult(Response); }
                if (method.Name == "StreamPostAsync")
                {
                    Options = (Options?)args[3];
                    foreach (var line in Lines) ((Action<string>)args[2]!)(line);
                    return AfterStream is null ? Task.CompletedTask : Task.FromException(AfterStream);
                }
                throw new NotSupportedException(method.Name);
            });
            Context = Stub<IPluginContext>.Create((method, args) => method.Name switch
            {
                "get_MetaData" => MetaData,
                "get_HttpService" => http,
                "get_Logger" => NullLogger.Instance,
                "LoadSettingStorage" => Settings,
                "SaveSettingStorage" => Save(),
                "GetTranslation" => args![0],
                "Dispose" => null,
                _ => throw new NotSupportedException(method.Name)
            });
            Main.Init(Context);
        }
        private object? Save() { Saves++; return null; }
        public void Dispose() => Main.Dispose();
    }
}

public class Stub<T> : DispatchProxy where T : class
{
    private Func<MethodInfo, object?[]?, object?> _handler = null!;
    public static T Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create<T, Stub<T>>(); ((Stub<T>)(object)proxy)._handler = handler; return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => _handler(method!, args);
}

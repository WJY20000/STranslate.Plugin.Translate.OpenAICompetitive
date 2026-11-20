using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Translate.OpenAICompetitive.View;
using STranslate.Plugin.Translate.OpenAICompetitive.ViewModel;
using System.Collections;
using STranslate.Plugin.Translate.OpenAICompetitive.Protocol;
using System.Windows.Controls;

namespace STranslate.Plugin.Translate.OpenAICompetitive;

public class Main : LlmTranslatePluginBase
{
    public const string DefaultDisplayName = "OpenAI Competitive";

    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public override void SelectPrompt(Prompt? prompt)
    {
        if (prompt is null || !Prompts.Contains(prompt)) return;
        base.SelectPrompt(prompt);

        // 保存到配置
        Settings.Prompts = [.. Prompts.Select(p => p.Clone())];
        Context.SaveSettingStorage<Settings>();
    }

    public override Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        UpdateDisplayName(Settings.DisplayName);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public static string NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? DefaultDisplayName : normalized;
    }

    public void UpdateDisplayName(string? displayName)
    {
        var normalized = NormalizeDisplayName(displayName);

        Settings.DisplayName = normalized;
        Context.MetaData.Name = normalized;
        TryUpdateHostServiceDisplayName(normalized);
    }

    public override string? GetSourceLanguage(LangEnum langEnum) => LanguageName(langEnum);
    public override string? GetTargetLanguage(LangEnum langEnum) => LanguageName(langEnum);

    private static string? LanguageName(LangEnum langEnum) => langEnum switch
    {
        LangEnum.Auto => "Requires you to identify automatically",
        LangEnum.ChineseSimplified => "Simplified Chinese",
        LangEnum.ChineseTraditional => "Traditional Chinese",
        LangEnum.Cantonese => "Cantonese",
        LangEnum.English => "English",
        LangEnum.Japanese => "Japanese",
        LangEnum.Korean => "Korean",
        LangEnum.French => "French",
        LangEnum.Spanish => "Spanish",
        LangEnum.Russian => "Russian",
        LangEnum.German => "German",
        LangEnum.Italian => "Italian",
        LangEnum.Turkish => "Turkish",
        LangEnum.PortuguesePortugal => "European Portuguese",
        LangEnum.PortugueseBrazil => "Brazilian Portuguese",
        LangEnum.Vietnamese => "Vietnamese",
        LangEnum.Indonesian => "Indonesian",
        LangEnum.Thai => "Thai",
        LangEnum.Malay => "Malay",
        LangEnum.Arabic => "Arabic",
        LangEnum.Hindi => "Hindi",
        LangEnum.MongolianCyrillic => "Mongolian (Cyrillic script)",
        LangEnum.MongolianTraditional => "Mongolian (traditional script)",
        LangEnum.Khmer => "Central Khmer",
        LangEnum.NorwegianBokmal => "Norwegian Bokmål",
        LangEnum.NorwegianNynorsk => "Norwegian Nynorsk",
        LangEnum.Persian => "Persian",
        LangEnum.Swedish => "Swedish",
        LangEnum.Polish => "Polish",
        LangEnum.Dutch => "Dutch",
        LangEnum.Ukrainian => "Ukrainian",
        LangEnum.Uzbek => "Uzbek",
        LangEnum.Uyghur => "Uyghur",
        _ => null
    };

    public override void Init(IPluginContext context)
    {
        _viewModel?.Dispose();
        _viewModel = null;
        _settingUi = null;
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
        Settings.Normalize();
        Context.MetaData.Name = Settings.DisplayName;
        Prompts.Clear();
        foreach (var prompt in Settings.Prompts)
            Prompts.Add(prompt);
    }

    public override void Dispose() => _viewModel?.Dispose();

    public override async Task TranslateAsync(TranslateRequest request, TranslateResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            result.Success(await TranslateCoreAsync(request, text => result.Text = text, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            result.Fail(ex.Message);
        }
    }

    public Task<string> ValidateApiAsync(CancellationToken cancellationToken = default) =>
        TranslateCoreAsync(new TranslateRequest("Hello world", LangEnum.English, LangEnum.ChineseSimplified),
            _ => { }, cancellationToken);

    private async Task<string> TranslateCoreAsync(TranslateRequest request, Action<string> onText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetSourceLanguage(request.SourceLang) ?? throw new InvalidOperationException(Context.GetTranslation("UnsupportedSourceLang"));
        var target = GetTargetLanguage(request.TargetLang) ?? throw new InvalidOperationException(Context.GetTranslation("UnsupportedTargetLang"));
        var url = ApiEndpoints.Chat(Settings.Url);
        var prompt = Prompts.FirstOrDefault(p => p.IsEnabled) ??
            throw new InvalidOperationException(Context.GetTranslation("NoEnabledPrompt"));
        if (prompt.Items is null || prompt.Items.Any(item => item is null))
            throw new InvalidOperationException("Prompt 消息配置无效，请重新编辑提示词。");

        // 在第一个 await 前建立请求快照，设置页修改不会改变正在重试的请求。
        var messages = prompt.Items.Select(item => new ChatMessage(item.Role,
            ChatProtocol.Expand(item.Content, source, target, request.Text))).ToArray();
        var stream = Settings.Stream;
        var payload = ChatProtocol.Request(Settings.Model, messages, stream, Settings.MaxTokens,
            Settings.Temperature, Settings.TopP, Settings.N);
        var options = new Options { Headers = ChatProtocol.Headers(Settings.ApiKey) };
        var hasOutput = false;
        return await RetryPolicy.ExecuteAsync(async () =>
        {
            var reader = new ChatResponseReader();
            if (stream)
            {
                await Context.HttpService.StreamPostAsync(url, payload, line =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reader.ProcessLine(line);
                    var text = reader.Text;
                    if (text.Length > 0)
                    {
                        hasOutput = true;
                        onText(text);
                    }
                }, options, cancellationToken);
            }
            else
            {
                var response = await Context.HttpService.PostAsync(url, payload, options, cancellationToken);
                reader.ReadResponse(response);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return reader.Complete();
        }, () => hasOutput, Settings.MaxRetries, Settings.RetryDelayMilliseconds, cancellationToken);
    }

    private static Type? _serviceManagerType;

    private bool TryUpdateHostServiceDisplayName(string displayName)
    {
        try
        {
            // 显示名每次按键都会同步，类型查找只需在宿主程序集加载后做一次。
            var serviceManagerType = _serviceManagerType ??= AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("STranslate.Core.ServiceManager", false))
                .FirstOrDefault(type => type != null);

            if (serviceManagerType is null)
            {
                Context.Logger.LogWarning("ServiceManager type was not found while syncing OpenAI Competitive display name.");
                return false;
            }

            var serviceManager = Ioc.Default.GetService(serviceManagerType);
            if (serviceManager is null)
            {
                Context.Logger.LogWarning("ServiceManager instance was not found while syncing OpenAI Competitive display name.");
                return false;
            }

            var allServicesProperty = serviceManagerType.GetProperty("AllServices");
            if (allServicesProperty?.GetValue(serviceManager) is not IEnumerable services)
            {
                Context.Logger.LogWarning("ServiceManager.AllServices was not available while syncing OpenAI Competitive display name.");
                return false;
            }

            foreach (var service in services)
            {
                var serviceType = service.GetType();
                var plugin = serviceType.GetProperty("Plugin")?.GetValue(service);

                if (!ReferenceEquals(plugin, this))
                    continue;

                var displayNameProperty = serviceType.GetProperty("DisplayName");
                if (displayNameProperty is null || !displayNameProperty.CanWrite)
                {
                    Context.Logger.LogWarning("Service.DisplayName was not writable while syncing OpenAI Competitive display name.");
                    return false;
                }

                if (!string.Equals(displayNameProperty.GetValue(service) as string, displayName, StringComparison.Ordinal))
                    displayNameProperty.SetValue(service, displayName);

                return true;
            }

            Context.Logger.LogWarning("Current OpenAI Competitive service was not found while syncing display name.");
        }
        catch (Exception ex)
        {
            Context.Logger.LogWarning(ex, "Failed to sync OpenAI Competitive display name to host service.");
        }

        return false;
    }
}

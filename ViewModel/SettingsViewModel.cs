using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Translate.OpenAICompetitive.Protocol;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace STranslate.Plugin.Translate.OpenAICompetitive.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private bool _updating;
    private bool _disposed;
    public Main Main { get; }
    public ObservableCollection<string> Models { get; }

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        Main = main;
        DisplayName = settings.DisplayName;
        Url = settings.Url;
        ApiKey = settings.ApiKey;
        Model = settings.Model;
        Models = new(settings.Models);
        Temperature = settings.Temperature;
        Stream = settings.Stream;
        MaxTokens = settings.MaxTokens;
        PropertyChanged += SaveProperty;
        Models.CollectionChanged += SaveModels;
    }

    [ObservableProperty] public partial string DisplayName { get; set; }
    [ObservableProperty] public partial string Url { get; set; }
    [ObservableProperty] public partial string ApiKey { get; set; }
    [ObservableProperty] public partial string? Model { get; set; }
    [ObservableProperty] public partial double Temperature { get; set; }
    [ObservableProperty] public partial bool Stream { get; set; }
    [ObservableProperty] public partial int MaxTokens { get; set; }
    [ObservableProperty] public partial string FetchModelsResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string ValidateResult { get; set; } = string.Empty;

    public string FinalUrl
    {
        get
        {
            try { return ApiEndpoints.Chat(Url); }
            catch (ArgumentException) { return "请填写有效的 API URL"; }
        }
    }

    private void SaveProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (_updating || _disposed) return;
        switch (e.PropertyName)
        {
            case nameof(DisplayName): Main.UpdateDisplayName(DisplayName); break;
            case nameof(Url):
                _settings.Url = Url;
                OnPropertyChanged(nameof(FinalUrl));
                break;
            case nameof(ApiKey): _settings.ApiKey = ApiKey; break;
            case nameof(Model): _settings.Model = Model?.Trim() ?? string.Empty; break;
            case nameof(Temperature): _settings.Temperature = Math.Round(Math.Clamp(Temperature, 0, 2), 1); break;
            case nameof(Stream): _settings.Stream = Stream; break;
            case nameof(MaxTokens): _settings.MaxTokens = Math.Max(0, MaxTokens); break;
            default: return;
        }
        _context.SaveSettingStorage<Settings>();
    }

    private void SaveModels(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_updating || _disposed) return;
        _settings.Models = [.. Models]; // 包括 Clear()/Reset，不能只处理 Add/Remove。
        _context.SaveSettingStorage<Settings>();
    }

    [RelayCommand]
    public void AddModel(string? model)
    {
        if (_updating || _disposed || string.IsNullOrWhiteSpace(model)) return;
        model = model.Trim();
        ChangeModels(() =>
        {
            if (!Models.Contains(model)) Models.Add(model);
            Model = model;
        });
    }

    [RelayCommand]
    private void DeleteModel(string? model)
    {
        if (_updating || _disposed || model is null || !Models.Contains(model)) return;
        var selected = Model;
        ChangeModels(() =>
        {
            Models.Remove(model);
            Model = selected == model ? Models.FirstOrDefault() ?? string.Empty : selected;
        });
    }

    private void ChangeModels(Action change)
    {
        _updating = true;
        try { change(); }
        finally { _updating = false; }
        _settings.Model = Model?.Trim() ?? string.Empty;
        _settings.Models = [.. Models];
        _context.SaveSettingStorage<Settings>();
    }

    [RelayCommand]
    private async Task FetchModelsAsync(CancellationToken cancellationToken)
    {
        var requestedUrl = Url;
        var requestedKey = ApiKey;
        FetchModelsResult = string.Empty;
        try
        {
            var response = await _context.HttpService.GetAsync(ApiEndpoints.Models(requestedUrl),
                new Options { Headers = ChatProtocol.Headers(requestedKey) }, cancellationToken);
            var ids = ChatProtocol.ReadModels(response);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            if (Url != requestedUrl || ApiKey != requestedKey)
            {
                FetchModelsResult = "API 配置已更改，请重新获取模型。";
                return;
            }
            var selected = Model?.Trim();
            ChangeModels(() =>
            {
                Models.Clear();
                foreach (var id in ids) Models.Add(id);
                // 模型列表可能不完整，保留用户手填的模型。
                if (!string.IsNullOrWhiteSpace(selected) && !Models.Contains(selected)) Models.Add(selected);
                Model = string.IsNullOrWhiteSpace(selected) ? Models[0] : selected;
            });
            FetchModelsResult = $"已获取 {ids.Count} 个模型";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed) FetchModelsResult = "获取模型失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void EditPrompt()
    {
        var dialog = _context.GetPromptEditWindow(Main.Prompts);
        if (dialog.ShowDialog() != true) return;
        _settings.Prompts = [.. Main.Prompts.Select(p => p.Clone())];
        _context.SaveSettingStorage<Settings>();
        Main.SelectedPrompt = Main.Prompts.FirstOrDefault(p => p.IsEnabled);
    }

    [RelayCommand]
    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var requested = (Url, ApiKey, Model, Stream, MaxTokens, Temperature);
        ValidateResult = string.Empty;
        try
        {
            await Main.ValidateApiAsync(cancellationToken);
            if (!_disposed)
                ValidateResult = requested == (Url, ApiKey, Model, Stream, MaxTokens, Temperature)
                    ? _context.GetTranslation("ValidationSuccess") : "配置已更改，请重新验证。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed) ValidateResult = _context.GetTranslation("ValidationFailure") + ": " + ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FetchModelsCommand.Cancel();
        ValidateCommand.Cancel();
        PropertyChanged -= SaveProperty;
        Models.CollectionChanged -= SaveModels;
    }
}

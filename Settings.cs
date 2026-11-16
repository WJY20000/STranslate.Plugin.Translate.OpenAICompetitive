namespace STranslate.Plugin.Translate.OpenAICompetitive;

public class Settings
{
    public string DisplayName { get; set; } = "OpenAI Competitive";
    public string ApiKey { get; set; } = string.Empty;
    public string Url { get; set; } = "https://api.openai.com/";
    public string Model { get; set; } = "gpt-4o";
    public List<string> Models { get; set; } = new List<string> { "gpt-4o", "gpt-5" };
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1;
    public int N { get; set; } = 1;
    public bool Stream { get; set; } = true;
    public int? MaxRetries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;

    public List<Prompt> Prompts { get; set; } = new List<Prompt>
    {
        new("翻译",
        [
            new PromptItem("system", "You are a professional, authentic translation engine. You only return the translated text, without any explanations."),
            new PromptItem("user", "Please translate from $source into $target (avoid explaining the original text):\r\n\r\n$content"),
        ], true),
        new("润色",
        [
            new PromptItem("system", "You are a professional, authentic text polishing engine. You only return the polished text, without any explanations."),
            new PromptItem("user", "Please polish the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
        new("总结",
        [
            new PromptItem("system", "You are a professional, authentic text summarization engine. You only return the summarized text, without any explanations."),
            new PromptItem("user", "Please summarize the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
    };

    public void Normalize()
    {
        DisplayName = Main.NormalizeDisplayName(DisplayName);
        Url ??= "https://api.openai.com/";
        ApiKey ??= string.Empty;
        Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4o" : Model.Trim();
        Models = (Models ?? []).Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (!Models.Contains(Model)) Models.Add(Model);
        Prompts ??= new Settings().Prompts;
        Prompts = Prompts.Where(p => p is not null).ToList();
        foreach (var prompt in Prompts)
        {
            prompt.Items ??= [];
            for (var index = prompt.Items.Count - 1; index >= 0; index--)
                if (prompt.Items[index] is null) prompt.Items.RemoveAt(index);
        }
        Temperature = double.IsFinite(Temperature) ? Math.Clamp(Temperature, 0, 2) : 0.7;
        TopP = double.IsFinite(TopP) ? Math.Clamp(TopP, 0, 1) : 1;
        MaxTokens = Math.Max(0, MaxTokens);
        N = Math.Clamp(N, 1, 128);
        MaxRetries = Math.Clamp(MaxRetries ?? 3, 0, 5);
        RetryDelayMilliseconds = Math.Clamp(RetryDelayMilliseconds, 0, 60000);
    }
}

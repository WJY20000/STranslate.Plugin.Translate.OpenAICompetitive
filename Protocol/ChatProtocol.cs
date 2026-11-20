using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Translate.OpenAICompetitive.Protocol;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public static partial class ChatProtocol
{
    // 单次替换，避免把用户原文中包含的占位符再次替换。
    public static string Expand(string? template, string source, string target, string text) =>
        Placeholder().Replace(template ?? string.Empty, match => match.Value switch
        {
            "$source" => source,
            "$target" => target,
            _ => text
        });

    public static Dictionary<string, object> Request(string? model, IEnumerable<ChatMessage> messages,
        bool stream, int maxTokens, double temperature, double topP, int n)
    {
        var selectedModel = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model.Trim();
        var items = messages.ToArray();
        if (items.Length == 0 || items.Any(m => string.IsNullOrWhiteSpace(m.Role)))
            throw new InvalidOperationException("请先完善 Prompt 配置：消息及角色不能为空。");
        var reasoningModel = ReasoningModel().IsMatch(selectedModel);
        // 推理模型只接受单个候选；插件也只展示第一个，避免请求被服务端拒绝。
        var candidates = reasoningModel ? 1 : Math.Clamp(n, 1, 128);
        var payload = new Dictionary<string, object>
        {
            ["model"] = selectedModel,
            ["messages"] = items,
            ["stream"] = stream,
            ["n"] = candidates
        };
        if (maxTokens > 0)
            payload[reasoningModel ? "max_completion_tokens" : "max_tokens"] = maxTokens;
        if (!reasoningModel)
        {
            payload["temperature"] = double.IsFinite(temperature) ? Math.Clamp(temperature, 0, 2) : 0.7;
            payload["top_p"] = double.IsFinite(topP) ? Math.Clamp(topP, 0, 1) : 1;
        }
        return payload;
    }

    public static Dictionary<string, string> Headers(string? apiKey)
    {
        var key = apiKey?.Trim();
        if (key?.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("API Key 不能包含换行符。");
        return string.IsNullOrEmpty(key) ? [] : new() { ["Authorization"] = "Bearer " + key };
    }

    public static IReadOnlyList<string> ReadModels(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("模型列表响应为空。");
        JsonObject root;
        try
        {
            root = JsonNode.Parse(response) as JsonObject ??
                   throw new InvalidDataException("模型列表不是有效的 JSON 对象。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("模型列表不是有效的 JSON 数据。", ex);
        }
        ThrowIfError(root);
        if (root["data"] is not JsonArray models)
            throw new InvalidDataException("模型列表缺少 data 数组。");
        // 兼容把模型名放在 model 字段上的网关（如部分 Gemini 兼容服务）。
        var ids = models.OfType<JsonObject>()
            .Select(item => ReadString(item, "id") ?? ReadString(item, "model"))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return ids.Length > 0 ? ids : throw new InvalidDataException("未获取到可用模型。");
    }

    private static string? ReadString(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    internal static void ThrowIfError(JsonObject root)
    {
        if (root["error"] is { } error)
        {
            var message = error is JsonObject obj ? obj["message"]?.ToString() : error.ToString();
            throw new InvalidOperationException("API 错误：" + (string.IsNullOrWhiteSpace(message) ? "服务端返回错误。" : message));
        }
    }

    [GeneratedRegex(@"\$(source|target|content)", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
    [GeneratedRegex(@"^(?:o[134](?:-|$)|gpt-5(?:[.-]|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReasoningModel();
}

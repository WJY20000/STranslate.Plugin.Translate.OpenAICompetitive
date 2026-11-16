using System.Text.RegularExpressions;

namespace STranslate.Plugin.Translate.OpenAICompetitive.Protocol;

public static partial class ApiEndpoints
{
    public static string Chat(string? url) => Build(url, "chat/completions");
    public static string Models(string? url) => Build(url, "models");

    private static string Build(string? url, string endpoint)
    {
        var input = url?.Trim() ?? string.Empty;
        var exact = input.EndsWith('#');
        if (exact) input = input[..^1].TrimEnd();
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("API URL 必须是有效的 HTTP/HTTPS 地址，且不能包含用户名、密码或片段。");

        if (exact && endpoint == "chat/completions") return uri.AbsoluteUri;
        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/chat/completions".Length];
        else if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/models".Length];
        else if (path.Length == 0 || path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            path += "/v1";
        else if (!VersionSuffix().IsMatch(path))
        {
            // 保留宿主 SDK 对完整自定义接口路径的既有行为。
            if (endpoint == "chat/completions") return uri.AbsoluteUri;
            throw new ArgumentException("无法从自定义接口推导模型列表地址，请使用 API 根地址、版本地址或完整 /chat/completions 地址。");
        }
        builder.Path = path + "/" + endpoint;
        return builder.Uri.AbsoluteUri;
    }

    [GeneratedRegex(@"/v\d+(?:[a-z][a-z0-9.-]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffix();
}

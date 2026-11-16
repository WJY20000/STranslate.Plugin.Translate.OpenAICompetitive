using System.Net;
using System.Net.Http;

namespace STranslate.Plugin.Translate.OpenAICompetitive.Protocol;

public static class RetryPolicy
{
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<bool> hasOutput,
        int? maxRetries, int delayMilliseconds, CancellationToken cancellationToken)
    {
        var retries = Math.Clamp(maxRetries ?? 3, 0, 5);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (Exception ex) when (attempt < retries && !hasOutput() &&
                !cancellationToken.IsCancellationRequested && IsTransient(ex))
            {
                await Task.Delay(Math.Clamp(delayMilliseconds, 0, 60000), cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException request => request.StatusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        TimeoutException => true,
        OperationCanceledException => true, // 调用者取消已在上方排除；这里只重试超时。
        _ => false
    };
}

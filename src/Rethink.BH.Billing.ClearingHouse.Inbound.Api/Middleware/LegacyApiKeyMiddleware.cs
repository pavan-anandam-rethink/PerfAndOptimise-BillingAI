namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;

/// <summary>Copies <c>XApiKey</c> onto <c>x-application-key</c> when the application key header is absent.</summary>
public sealed class LegacyApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    public LegacyApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Copies the legacy header, then continues the pipeline.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Request.Headers;
        if (!headers.ContainsKey(ApiHeaderNames.ApplicationKey)
            && headers.TryGetValue(ApiHeaderNames.LegacyApiKey, out var apiKey)
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            headers[ApiHeaderNames.ApplicationKey] = apiKey.ToString();
        }

        return _next(context);
    }
}

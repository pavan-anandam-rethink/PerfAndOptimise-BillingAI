using Microsoft.Extensions.Configuration;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>
/// Billing API settings. The root <c>BillingApiUrl</c> and <c>BillingApiKey</c> may be Key Vault secret names;
/// the <c>BillingApiClient</c> section values are fallbacks used as literals, as in the legacy client.
/// </summary>
public sealed class BillingApiOptions
{
    /// <summary>Default application key header.</summary>
    public const string DefaultHeaderName = "x-application-key";

    /// <summary>Legacy section that may hold <c>ApiBaseUrl</c> and <c>ApiKey</c>.</summary>
    public const string LegacySectionName = "BillingApiClient";

    /// <summary>Configured <c>BillingApiUrl</c> (literal URL or secret name).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Configured <c>BillingApiKey</c> (literal or secret name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Literal <c>BillingApiClient:ApiBaseUrl</c>, used when <see cref="BaseUrl"/> is blank.</summary>
    public string? FallbackBaseUrl { get; set; }

    /// <summary>Literal <c>BillingApiClient:ApiKey</c>, used when <see cref="ApiKey"/> is blank.</summary>
    public string? FallbackApiKey { get; set; }

    /// <summary>Header that carries the application key, from <c>HeaderKey</c>.</summary>
    public string HeaderName { get; set; } = DefaultHeaderName;

    /// <summary>Reads the legacy keys.</summary>
    public void Configure(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        BaseUrl = configuration["BillingApiUrl"];
        ApiKey = configuration["BillingApiKey"];
        FallbackBaseUrl = configuration[$"{LegacySectionName}:ApiBaseUrl"];
        FallbackApiKey = configuration[$"{LegacySectionName}:ApiKey"];
        var headerName = configuration["HeaderKey"];
        HeaderName = string.IsNullOrWhiteSpace(headerName) ? DefaultHeaderName : headerName;
    }
}

using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>Resolves the Billing address and key with the legacy secret-name rule. Secret values are cached by the resolver.</summary>
public sealed class BillingApiSettingsProvider : IBillingApiSettingsProvider
{
    private const string BaseUrlSetting = "BillingApiUrl";
    private const string ApiKeySetting = "BillingApiKey";

    private readonly BillingApiOptions _options;
    private readonly ISecretResolver _secretResolver;

    /// <summary>Creates the provider.</summary>
    public BillingApiSettingsProvider(IOptions<BillingApiOptions> options, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public async Task<BillingApiSettings> GetAsync(CancellationToken cancellationToken)
    {
        var baseAddress = await ResolveBaseAddressAsync(cancellationToken).ConfigureAwait(false);
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? NullIfBlank(_options.FallbackApiKey)
            : await _secretResolver.ResolveAsync(_options.ApiKey, ApiKeySetting, SecretNamePatternPolicy.Instance, cancellationToken).ConfigureAwait(false);

        return new BillingApiSettings(baseAddress, apiKey, _options.HeaderName);
    }

    private async Task<Uri> ResolveBaseAddressAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return Uri.TryCreate(_options.FallbackBaseUrl, UriKind.Absolute, out var fallback)
                ? fallback
                : throw new InvalidOperationException($"{BaseUrlSetting} is not configured.");
        }

        var resolved = await _secretResolver
            .ResolveAsync(_options.BaseUrl, BaseUrlSetting, SecretNamePatternPolicy.Instance, cancellationToken)
            .ConfigureAwait(false);

        return Uri.TryCreate(resolved, UriKind.Absolute, out var baseAddress)
            ? baseAddress
            : throw new InvalidOperationException($"{BaseUrlSetting} must resolve to an absolute URI.");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

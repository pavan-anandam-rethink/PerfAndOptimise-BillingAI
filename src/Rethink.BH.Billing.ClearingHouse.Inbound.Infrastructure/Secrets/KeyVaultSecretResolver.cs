using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>Resolves configured values through Key Vault and caches secret values for <see cref="KeyVaultOptions.SecretCacheMinutes"/>.</summary>
public sealed class KeyVaultSecretResolver : ISecretResolver
{
    private const string CacheKeyPrefix = "keyvault-secret:";
    private const int NotFoundStatus = 404;

    private readonly SecretClient? _secretClient;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _cacheDuration;

    /// <summary>Creates the resolver.</summary>
    public KeyVaultSecretResolver(KeyVaultClientAccessor keyVault, IMemoryCache memoryCache, IOptions<KeyVaultOptions> options)
    {
        ArgumentNullException.ThrowIfNull(keyVault);
        ArgumentNullException.ThrowIfNull(memoryCache);
        ArgumentNullException.ThrowIfNull(options);

        _secretClient = keyVault.Client;
        _memoryCache = memoryCache;
        _cacheDuration = TimeSpan.FromMinutes(options.Value.SecretCacheMinutes);
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(string? configuredValue, string settingName, ISecretReferencePolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new InvalidOperationException($"{settingName} is not configured.");
        }

        if (!policy.IsSecretReference(configuredValue))
        {
            return configuredValue;
        }

        if (_secretClient is null)
        {
            throw new InvalidOperationException($"{settingName} '{configuredValue}' is a Key Vault secret name, but KeyVaultUri is not configured.");
        }

        var secretValue = await ReadCachedAsync(_secretClient, configuredValue, settingName, cancellationToken).ConfigureAwait(false);
        var normalized = policy.NormalizeSecretValue(secretValue ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            _memoryCache.Remove(CacheKeyPrefix + configuredValue);
            throw new InvalidOperationException($"Key Vault secret '{configuredValue}' is empty.");
        }

        return normalized;
    }

    private Task<string?> ReadCachedAsync(SecretClient secretClient, string secretName, string settingName, CancellationToken cancellationToken)
    {
        if (_cacheDuration <= TimeSpan.Zero)
        {
            return ReadAsync(secretClient, secretName, settingName, cancellationToken);
        }

        return _memoryCache.GetOrCreateAsync(CacheKeyPrefix + secretName, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            return ReadAsync(secretClient, secretName, settingName, cancellationToken);
        });
    }

    private static async Task<string?> ReadAsync(SecretClient secretClient, string secretName, string settingName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value.Value;
        }
        catch (RequestFailedException notFound) when (notFound.Status == NotFoundStatus)
        {
            throw new InvalidOperationException($"Key Vault secret '{secretName}' was not found for {settingName}.", notFound);
        }
    }
}

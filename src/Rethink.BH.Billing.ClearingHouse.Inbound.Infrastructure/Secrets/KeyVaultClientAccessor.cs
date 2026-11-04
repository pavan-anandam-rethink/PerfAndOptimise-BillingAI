using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>
/// Holds the process-wide Key Vault client. The vault comes from <c>KEY_VAULT_NAME</c> when Helm sets it,
/// otherwise from <c>KeyVaultUri</c>. <see cref="Client"/> is null when neither is configured.
/// </summary>
public sealed class KeyVaultClientAccessor
{
    /// <summary>Helm placeholder that means no vault is configured.</summary>
    public const string NotConfiguredVaultName = "NotConfigured";

    /// <summary>Host suffix of an Azure Key Vault name.</summary>
    public const string VaultHostSuffix = ".vault.azure.net";

    /// <summary>Creates the accessor around an existing client.</summary>
    public KeyVaultClientAccessor(SecretClient? client)
    {
        Client = client;
    }

    /// <summary>Shared Key Vault client, or null when no vault is configured.</summary>
    public SecretClient? Client { get; }

    /// <summary>Creates the accessor from configuration using <see cref="DefaultAzureCredential"/> (workload identity in AKS).</summary>
    public static KeyVaultClientAccessor FromConfiguration(IConfiguration configuration)
    {
        var vaultUri = ResolveVaultUri(configuration);
        return new KeyVaultClientAccessor(vaultUri is null ? null : new SecretClient(vaultUri, new DefaultAzureCredential()));
    }

    /// <summary>Returns the vault URI, or null when no vault is configured.</summary>
    public static Uri? ResolveVaultUri(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var vaultName = configuration["KEY_VAULT_NAME"];
        var configured = !string.IsNullOrWhiteSpace(vaultName)
            && !string.Equals(vaultName, NotConfiguredVaultName, StringComparison.OrdinalIgnoreCase)
                ? $"https://{vaultName}{VaultHostSuffix}/"
                : configuration["KeyVaultUri"];

        return Uri.TryCreate(configured, UriKind.Absolute, out var vaultUri) ? vaultUri : null;
    }
}

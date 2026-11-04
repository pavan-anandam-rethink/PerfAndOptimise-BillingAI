using System.ComponentModel.DataAnnotations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>Key Vault settings, bound from the <c>KeyVault</c> section.</summary>
public sealed class KeyVaultOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "KeyVault";

    /// <summary>How long a secret value is reused before Key Vault is read again. Zero disables caching.</summary>
    [Range(0, 1440)]
    public int SecretCacheMinutes { get; set; } = 15;
}

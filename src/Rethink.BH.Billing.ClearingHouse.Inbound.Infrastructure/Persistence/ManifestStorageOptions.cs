using System.ComponentModel.DataAnnotations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>Manifest database settings.</summary>
public sealed class ManifestStorageOptions
{
    /// <summary>Configuration key of the connection string.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:InboundManifest";

    /// <summary>Section holding the timeout and retry settings.</summary>
    public const string SectionName = "Pipeline:Manifest";

    /// <summary>
    /// SQL connection string, or a Key Vault secret name when it has no <c>=</c>. Use
    /// <c>Authentication=Active Directory Default</c> so the workload identity signs in and no password is stored.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Command timeout in seconds.</summary>
    [Range(5, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Transient-fault retries for SQL commands.</summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 5;
}

using Microsoft.Extensions.Configuration;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;

/// <summary>Backup container written before Billing receives a file.</summary>
public sealed class BackupStorageOptions
{
    /// <summary>Configuration key of the container name.</summary>
    public const string ContainerNameKey = "AvailityBackup";

    /// <summary>Configuration key of the blob connection string.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:BlobStorage:ConnectionString";

    /// <summary>Fallback connection string key used when the primary key is blank.</summary>
    public const string FallbackConnectionStringKey = "EligibilityProcessing:Blob:ConnectionString";

    /// <summary>Container name from <c>AvailityBackup</c>.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Blob connection string, or a Key Vault secret name when it has no <c>=</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>True when both values are present.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(ContainerName) && !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>Reads the legacy keys, falling back to the eligibility blob connection when the primary is blank.</summary>
    public void Configure(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ContainerName = configuration[ContainerNameKey] ?? string.Empty;
        var connection = configuration[ConnectionStringKey];
        ConnectionString = (string.IsNullOrWhiteSpace(connection) ? configuration[FallbackConnectionStringKey] : connection) ?? string.Empty;
    }
}

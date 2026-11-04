namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;

/// <summary>
/// One clearing house under <c>Clearinghouses</c>. SFTP values are literals or Key Vault secret names
/// (see <see cref="Secrets.LiteralWhenContainsEqualsPolicy"/>).
/// </summary>
public sealed class ClearingHouseSettings
{
    /// <summary>Clearing house id posted by the scheduler.</summary>
    public int Id { get; set; }

    /// <summary>Title sent to Billing, for example Availity or Stedi.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Child section name under <c>Clearinghouses</c>. Set by the binder, not by configuration.</summary>
    public string SectionKey { get; set; } = string.Empty;

    /// <summary>SFTP host, or a Key Vault secret name.</summary>
    public string Hosts { get; set; } = string.Empty;

    /// <summary>SFTP port, or a Key Vault secret name.</summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>SFTP user, or a Key Vault secret name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>SFTP password, or a Key Vault secret name.</summary>
    public string UserPassword { get; set; } = string.Empty;

    /// <summary>SFTP download directory, or a Key Vault secret name.</summary>
    public string DownloadDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Optional SHA-256 host key fingerprint, as printed by <c>ssh-keygen -lf</c> (with or without the <c>SHA256:</c> prefix).
    /// When set, connections to any other host key are refused. A value made only of letters, digits, and dashes is read from Key Vault.
    /// </summary>
    public string HostKeyFingerprint { get; set; } = string.Empty;

    /// <summary>Configuration path of this clearing house.</summary>
    public string ConfigurationPath => $"{ClearingHouseCatalogOptions.SectionName}:{SectionKey}";

    /// <summary>True when every SFTP value the download needs is present.</summary>
    public bool IsComplete =>
        Id > 0
        && !string.IsNullOrWhiteSpace(Title)
        && !string.IsNullOrWhiteSpace(Hosts)
        && !string.IsNullOrWhiteSpace(Port)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(UserPassword)
        && !string.IsNullOrWhiteSpace(DownloadDirectory);
}

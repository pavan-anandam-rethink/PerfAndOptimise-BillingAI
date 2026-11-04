namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Resolved SFTP connection settings. <see cref="ToString"/> never includes the user name or password.</summary>
public sealed class SftpCredentials
{
    /// <summary>Creates the credentials.</summary>
    public SftpCredentials(string host, int port, string userName, string password, string downloadDirectory, string? hostKeyFingerprint)
    {
        Host = host;
        Port = port;
        UserName = userName;
        Password = password;
        DownloadDirectory = downloadDirectory;
        HostKeyFingerprint = hostKeyFingerprint;
    }

    /// <summary>SFTP host.</summary>
    public string Host { get; }

    /// <summary>SFTP port.</summary>
    public int Port { get; }

    /// <summary>SFTP user.</summary>
    public string UserName { get; }

    /// <summary>SFTP password.</summary>
    public string Password { get; }

    /// <summary>Directory polled for response files.</summary>
    public string DownloadDirectory { get; }

    /// <summary>Pinned SHA-256 host key fingerprint, or null to accept any host key.</summary>
    public string? HostKeyFingerprint { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Host}:{Port} ({DownloadDirectory})";
}

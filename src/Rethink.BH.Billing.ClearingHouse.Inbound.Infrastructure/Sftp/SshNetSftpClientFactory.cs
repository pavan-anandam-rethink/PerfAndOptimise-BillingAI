using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Creates SSH.NET clients with password authentication and the pinned host key check.</summary>
public sealed class SshNetSftpClientFactory : ISftpClientFactory
{
    private readonly ILogger<SshNetSftpClientFactory> _logger;

    /// <summary>Creates the factory.</summary>
    public SshNetSftpClientFactory(ILogger<SshNetSftpClientFactory> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ISftpClient Create(SftpCredentials credentials, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var connectionInfo = new ConnectionInfo(
            credentials.Host,
            credentials.Port,
            credentials.UserName,
            new PasswordAuthenticationMethod(credentials.UserName, credentials.Password))
        {
            Timeout = connectTimeout,
        };

        var client = new SftpClient(connectionInfo);
        client.HostKeyReceived += (_, hostKey) => hostKey.CanTrust = ShouldTrust(credentials, hostKey.FingerPrintSHA256);
        return client;
    }

    /// <summary>True when the presented key matches the pin (or nothing is pinned); a mismatch is logged as an error.</summary>
    internal bool ShouldTrust(SftpCredentials credentials, string presentedFingerprintSha256)
    {
        var trusted = HostKeyVerifier.IsTrusted(presentedFingerprintSha256, credentials.HostKeyFingerprint);
        if (!trusted)
        {
            InfrastructureLog.SftpHostKeyRejected(_logger, credentials.Host, presentedFingerprintSha256);
        }

        return trusted;
    }
}

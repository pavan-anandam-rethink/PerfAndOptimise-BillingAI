using Renci.SshNet;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Creates unconnected SFTP clients. The seam lets the pool be tested without a server.</summary>
public interface ISftpClientFactory
{
    /// <summary>Creates a client for <paramref name="credentials"/> with the given connect timeout and host key check.</summary>
    ISftpClient Create(SftpCredentials credentials, TimeSpan connectTimeout);
}

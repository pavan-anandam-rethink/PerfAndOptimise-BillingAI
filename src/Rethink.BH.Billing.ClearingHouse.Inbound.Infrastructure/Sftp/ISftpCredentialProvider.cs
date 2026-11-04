namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Resolves the SFTP settings of a configured clearing house.</summary>
public interface ISftpCredentialProvider
{
    /// <summary>Returns host, credentials, and download directory. Callers must not log the result's secrets.</summary>
    Task<SftpCredentials> GetAsync(int clearingHouseId, CancellationToken cancellationToken);
}

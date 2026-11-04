using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Opens an SFTP session on a clearing house download directory.</summary>
public interface ISftpSessionFactory
{
    /// <summary>Connects to the clearing house. Throws when the first connection cannot be established.</summary>
    Task<ISftpSession> OpenAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken);
}

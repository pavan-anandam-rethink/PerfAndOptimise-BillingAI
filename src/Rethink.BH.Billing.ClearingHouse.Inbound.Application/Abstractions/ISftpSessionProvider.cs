using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>
/// Long-lived SFTP sessions shared by queue workers, one per clearing house, so each message reuses pooled connections
/// instead of paying an SSH handshake. Callers must not dispose the returned session; the provider owns it.
/// </summary>
public interface ISftpSessionProvider
{
    /// <summary>Returns the shared session, opening it on first use. A failed open is not cached.</summary>
    Task<ISftpSession> GetAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken);
}

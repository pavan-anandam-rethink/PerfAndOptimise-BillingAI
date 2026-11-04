using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Moves one remote file through download, backup, Billing registration, and remote delete.</summary>
public interface IInboundFileProcessor
{
    /// <summary>
    /// Processes <paramref name="file"/>. Expected business failures are returned as a failed outcome;
    /// infrastructure failures are thrown and captured by the caller's fault boundary.
    /// </summary>
    Task<InboundFileOutcome> ProcessAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        RemoteFile file,
        InboundFileProgress progress,
        CancellationToken cancellationToken);
}

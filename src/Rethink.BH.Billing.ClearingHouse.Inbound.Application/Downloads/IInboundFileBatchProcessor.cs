using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Processes the selected files of one run with bounded parallelism.</summary>
public interface IInboundFileBatchProcessor
{
    /// <summary>
    /// Processes <paramref name="files"/>. When <paramref name="runToken"/> expires, the outcomes finished so far are returned.
    /// Cancellation of <paramref name="callerToken"/> propagates.
    /// </summary>
    Task<IReadOnlyList<InboundFileOutcome>> ProcessAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        IReadOnlyList<RemoteFile> files,
        CancellationToken runToken,
        CancellationToken callerToken);
}

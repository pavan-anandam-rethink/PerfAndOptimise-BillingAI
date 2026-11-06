using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Durable record of every listed file and its state. The only source of truth for "has this file been handled".</summary>
public interface IInboundFileManifest
{
    /// <summary>
    /// Registers files not seen before and returns every listed file still in <see cref="InboundFileState.Discovered"/>:
    /// the new ones plus any a previous lister run registered but did not enqueue.
    /// </summary>
    Task<IReadOnlyList<InboundFileEntry>> RegisterAsync(int clearingHouseId, IReadOnlyList<RemoteFile> files, CancellationToken cancellationToken);

    /// <summary>Moves <paramref name="fileIds"/> from Discovered to Queued.</summary>
    Task MarkQueuedAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);

    /// <summary>Returns the file, or null when the id is unknown.</summary>
    Task<InboundFileEntry?> FindAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>Applies <paramref name="transition"/> when the file is in one of its from-states. Returns false when it is not.</summary>
    Task<bool> TryTransitionAsync(InboundFileTransition transition, CancellationToken cancellationToken);

    /// <summary>Counts by state and the oldest unfinished file.</summary>
    Task<PipelineBacklog> GetBacklogAsync(CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="maxCount"/> files in <paramref name="state"/>, oldest first.</summary>
    Task<IReadOnlyList<InboundFileEntry>> ListByStateAsync(InboundFileState state, int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="maxCount"/> files of <paramref name="clearingHouseId"/> in <paramref name="states"/> whose last
    /// change is older than <paramref name="updatedBeforeUtc"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<InboundFileEntry>> ListStalledAsync(
        int clearingHouseId,
        IReadOnlyCollection<InboundFileState> states,
        DateTime updatedBeforeUtc,
        int maxCount,
        CancellationToken cancellationToken);
}

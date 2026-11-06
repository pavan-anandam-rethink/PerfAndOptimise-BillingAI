using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// One listing: list the directory, keep files old enough to be complete, let the manifest drop files it has already
/// seen, send one download message per remaining file, then re-send stalled files. A file is marked Queued only after its
/// message is sent, so a crash in between is repaired by the next run.
/// </summary>
public sealed class InboundFileLister
{
    private readonly ISftpSessionFactory _sftpSessionFactory;
    private readonly IInboundFileManifest _manifest;
    private readonly IInboundWorkSender _workSender;
    private readonly StalledFileReconciler _reconciler;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumFileAge;
    private readonly ILogger<InboundFileLister> _logger;

    /// <summary>Creates the lister.</summary>
    public InboundFileLister(
        ISftpSessionFactory sftpSessionFactory,
        IInboundFileManifest manifest,
        IInboundWorkSender workSender,
        StalledFileReconciler reconciler,
        TimeProvider timeProvider,
        IOptions<PipelineOptions> options,
        ILogger<InboundFileLister> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sftpSessionFactory = sftpSessionFactory;
        _manifest = manifest;
        _workSender = workSender;
        _reconciler = reconciler;
        _timeProvider = timeProvider;
        _minimumFileAge = options.Value.MinimumFileAge;
        _logger = logger;
    }

    /// <summary>Lists <paramref name="clearingHouse"/> and enqueues its new files.</summary>
    public async Task<InboundListingResult> ListAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);

        var listed = await ListRemoteAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
        var stable = RemoteFileSelector.SelectStable(listed, _timeProvider.GetUtcNow().UtcDateTime, _minimumFileAge);
        var enqueued = await EnqueueNewAsync(clearingHouse.Id, stable, cancellationToken).ConfigureAwait(false);
        var requeued = await _reconciler.RequeueAsync(clearingHouse.Id, cancellationToken).ConfigureAwait(false);

        PipelineLog.ListingCompleted(_logger, clearingHouse.Title, listed.Count, stable.Count, enqueued, requeued);
        return new InboundListingResult(clearingHouse.Id, InboundListingKind.Completed, listed.Count, stable.Count, enqueued, requeued);
    }

    private async Task<IReadOnlyList<RemoteFile>> ListRemoteAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        var session = await _sftpSessionFactory.OpenAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            return await session.ListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> EnqueueNewAsync(int clearingHouseId, IReadOnlyList<RemoteFile> stable, CancellationToken cancellationToken)
    {
        var pending = await _manifest.RegisterAsync(clearingHouseId, stable, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return 0;
        }

        await _workSender.SendAsync(InboundWorkStage.Download, pending.Select(InboundFileWorkItem.First).ToArray(), cancellationToken).ConfigureAwait(false);
        await _manifest.MarkQueuedAsync(pending.Select(entry => entry.Id).ToArray(), cancellationToken).ConfigureAwait(false);
        return pending.Count;
    }
}

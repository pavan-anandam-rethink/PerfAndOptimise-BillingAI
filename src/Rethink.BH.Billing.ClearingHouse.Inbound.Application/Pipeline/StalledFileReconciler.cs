using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Crash recovery. A worker that dies between two steps leaves a file in an unfinished state with no message; each lister
/// run finds such files (untouched for <c>StalledAfterMinutes</c>) and sends them to the stage that continues them.
/// Duplicates are harmless because handlers claim through the manifest. In Shadow mode only downloads are recovered.
/// </summary>
public sealed class StalledFileReconciler
{
    private readonly IInboundFileManifest _manifest;
    private readonly IInboundWorkSender _workSender;
    private readonly TimeProvider _timeProvider;
    private readonly PipelineOptions _options;
    private readonly ILogger<StalledFileReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    public StalledFileReconciler(
        IInboundFileManifest manifest,
        IInboundWorkSender workSender,
        TimeProvider timeProvider,
        IOptions<PipelineOptions> options,
        ILogger<StalledFileReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _manifest = manifest;
        _workSender = workSender;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Re-sends stalled files of <paramref name="clearingHouseId"/> and returns how many were sent.</summary>
    public async Task<int> RequeueAsync(int clearingHouseId, CancellationToken cancellationToken)
    {
        var states = _options.IsActive ? PipelineStages.AllRecoveryStates : PipelineStages.DownloadRecoveryStates;
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _options.StalledAfter;
        var stalled = await _manifest
            .ListStalledAsync(clearingHouseId, states, cutoff, _options.StalledBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (stalled.Count == 0)
        {
            return 0;
        }

        await SendByStageAsync(stalled, InboundWorkStage.Download, cancellationToken).ConfigureAwait(false);
        await SendByStageAsync(stalled, InboundWorkStage.Process, cancellationToken).ConfigureAwait(false);
        PipelineLog.StalledFilesRequeued(_logger, stalled.Count, _options.StalledAfterMinutes);
        return stalled.Count;
    }

    private Task SendByStageAsync(IReadOnlyList<InboundFileEntry> stalled, InboundWorkStage stage, CancellationToken cancellationToken)
    {
        var items = stalled
            .Where(entry => PipelineStages.StageFor(entry.State) == stage)
            .Select(InboundFileWorkItem.First)
            .ToArray();

        return items.Length == 0 ? Task.CompletedTask : _workSender.SendAsync(stage, items, cancellationToken);
    }
}

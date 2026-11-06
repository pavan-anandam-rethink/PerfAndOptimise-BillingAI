using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Turns an at-least-once message into exactly-one ownership of the file: the manifest compare-and-set moves the file into
/// its in-progress state with a claim expiry. Duplicates, late messages, and messages for finished files are completed
/// without doing work; a file in progress can be reclaimed only after its claim expired (the previous worker crashed).
/// </summary>
public sealed class WorkItemClaimer
{
    private readonly IInboundFileManifest _manifest;
    private readonly IClearingHouseCatalog _clearingHouseCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _claimTimeout;
    private readonly ILogger<WorkItemClaimer> _logger;

    /// <summary>Creates the claimer.</summary>
    public WorkItemClaimer(
        IInboundFileManifest manifest,
        IClearingHouseCatalog clearingHouseCatalog,
        TimeProvider timeProvider,
        IOptions<PipelineOptions> options,
        ILogger<WorkItemClaimer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _manifest = manifest;
        _clearingHouseCatalog = clearingHouseCatalog;
        _timeProvider = timeProvider;
        _claimTimeout = options.Value.ClaimTimeout;
        _logger = logger;
    }

    /// <summary>
    /// Claims the file of <paramref name="workItem"/>. <paramref name="claimStateFor"/> maps the current state to the
    /// in-progress state this stage moves it into, or null when this stage has nothing to do in that state.
    /// </summary>
    public async Task<WorkItemClaim> ClaimAsync(
        InboundWorkStage stage,
        InboundFileWorkItem workItem,
        Func<InboundFileState, InboundFileState?> claimStateFor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(claimStateFor);

        var entry = await _manifest.FindAsync(workItem.FileId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            PipelineLog.WorkItemOrphaned(_logger, stage.ToString());
            return WorkItemClaim.NotClaimed(WorkItemDisposition.Completed);
        }

        if (claimStateFor(entry.State) is not { } claimedState)
        {
            PipelineLog.WorkItemAlreadyHandled(_logger, stage.ToString(), entry.FileName, entry.State.ToString());
            return WorkItemClaim.NotClaimed(WorkItemDisposition.Completed);
        }

        var clearingHouse = _clearingHouseCatalog.FindById(entry.ClearingHouseId);
        if (clearingHouse is null)
        {
            return await QuarantineUnknownClearingHouseAsync(stage, entry, cancellationToken).ConfigureAwait(false);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var claimExpires = now + _claimTimeout;
        var transition = new InboundFileTransition
        {
            FileId = entry.Id,
            FromStates = entry.State == claimedState ? [] : [entry.State],
            ToState = claimedState,
            ReclaimExpiredBeforeUtc = now,
            ClaimExpiresUtc = claimExpires,
        };

        if (!await _manifest.TryTransitionAsync(transition, cancellationToken).ConfigureAwait(false))
        {
            PipelineLog.WorkItemClaimLost(_logger, stage.ToString(), entry.FileName);
            return WorkItemClaim.NotClaimed(WorkItemDisposition.Completed);
        }

        return WorkItemClaim.Claimed(entry with { State = claimedState, ClaimExpiresUtc = claimExpires }, clearingHouse);
    }

    private async Task<WorkItemClaim> QuarantineUnknownClearingHouseAsync(InboundWorkStage stage, InboundFileEntry entry, CancellationToken cancellationToken)
    {
        PipelineLog.WorkItemClearingHouseMissing(_logger, stage.ToString(), entry.ClearingHouseId);
        var quarantine = InboundFileTransition.Create(entry.Id, InboundFileState.Quarantined, entry.State) with
        {
            LastError = $"Clearing house {entry.ClearingHouseId} is not configured.",
        };
        await _manifest.TryTransitionAsync(quarantine, cancellationToken).ConfigureAwait(false);
        return WorkItemClaim.NotClaimed(WorkItemDisposition.Quarantined);
    }
}

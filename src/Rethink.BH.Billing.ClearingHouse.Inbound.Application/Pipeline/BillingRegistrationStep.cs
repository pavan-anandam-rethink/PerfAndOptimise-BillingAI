using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Registers a file claimed in Processing with Billing. Exceptions, rejections, and "not queued" answers are retried with
/// backoff, exactly as the legacy flow leaves such files for the next run. Once Billing queued the file it moves to
/// Deleting with <c>BillingQueuedUtc</c> set, so it is never sent to Billing again, and the finalizer runs.
/// </summary>
public sealed class BillingRegistrationStep
{
    private readonly ILandedFileRegistrar _registrar;
    private readonly RemoteFileFinalizer _finalizer;
    private readonly IInboundFileManifest _manifest;
    private readonly IWorkItemFailureHandler _failureHandler;
    private readonly IFaultBoundary _faultBoundary;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _claimTimeout;
    private readonly ILogger<BillingRegistrationStep> _logger;

    /// <summary>Creates the step.</summary>
    public BillingRegistrationStep(
        ILandedFileRegistrar registrar,
        RemoteFileFinalizer finalizer,
        IInboundFileManifest manifest,
        IWorkItemFailureHandler failureHandler,
        IFaultBoundary faultBoundary,
        TimeProvider timeProvider,
        IOptions<PipelineOptions> options,
        ILogger<BillingRegistrationStep> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _registrar = registrar;
        _finalizer = finalizer;
        _manifest = manifest;
        _failureHandler = failureHandler;
        _faultBoundary = faultBoundary;
        _timeProvider = timeProvider;
        _claimTimeout = options.Value.ClaimTimeout;
        _logger = logger;
    }

    /// <summary>Registers the claimed file and finalizes it when Billing queued it.</summary>
    public async Task<WorkItemDisposition> RunAsync(InboundFileWorkItem workItem, WorkItemClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.IsClaimed)
        {
            return claim.Disposition;
        }

        var entry = claim.Entry;
        var clearingHouse = claim.ClearingHouse;
        var registered = await _faultBoundary
            .RunAsync(OperationNames.RegisterLandedFile, () => _registrar.RegisterAsync(clearingHouse, entry, cancellationToken))
            .ConfigureAwait(false);

        var reason = registered.IsSucceeded ? RejectionReason(registered.Value!) : registered.Exception?.Message ?? InboundFailureMessages.BillingRejected;
        if (reason is not null)
        {
            var failure = new WorkItemFailure(InboundWorkStage.Process, workItem, entry, InboundFileState.Processing, InboundFileState.Landed, reason);
            return await _failureHandler.HandleAsync(failure, cancellationToken).ConfigureAwait(false);
        }

        var deleting = await MarkBillingQueuedAsync(claim, registered.Value!.FileId, cancellationToken).ConfigureAwait(false);
        return await _finalizer.FinishAsync(workItem, deleting, cancellationToken).ConfigureAwait(false);
    }

    private static string? RejectionReason(BillingRegistration registration)
    {
        if (!registration.Accepted)
        {
            return registration.Failure ?? InboundFailureMessages.BillingRejected;
        }

        return registration.Queued ? null : InboundFailureMessages.BillingDidNotQueue;
    }

    private async Task<WorkItemClaim> MarkBillingQueuedAsync(WorkItemClaim claim, int? billingFileId, CancellationToken cancellationToken)
    {
        var entry = claim.Entry!;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var claimExpires = now + _claimTimeout;
        var queued = InboundFileTransition.Create(entry.Id, InboundFileState.Deleting, InboundFileState.Processing) with
        {
            BillingFileId = billingFileId,
            BillingQueuedUtc = now,
            ClaimExpiresUtc = claimExpires,
        };
        await _manifest.TryTransitionAsync(queued, cancellationToken).ConfigureAwait(false);
        PipelineLog.FileRegistered(_logger, entry.FileName, billingFileId, nameof(InboundFileState.BillingQueued));

        var updated = entry with { State = InboundFileState.Deleting, ClaimExpiresUtc = claimExpires, BillingQueuedUtc = now, BillingFileId = billingFileId };
        return WorkItemClaim.Claimed(updated, claim.ClearingHouse!);
    }
}

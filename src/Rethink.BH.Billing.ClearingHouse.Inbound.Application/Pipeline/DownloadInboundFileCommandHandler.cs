using MediatR;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Download worker entry point: claims the file, lands it inside the fault boundary, and either records the landing or
/// hands the failure to the retry and quarantine policy.
/// </summary>
public sealed class DownloadInboundFileCommandHandler : IRequestHandler<DownloadInboundFileCommand, WorkItemDisposition>
{
    private readonly WorkItemClaimer _claimer;
    private readonly InboundFileLander _lander;
    private readonly IWorkItemFailureHandler _failureHandler;
    private readonly IFaultBoundary _faultBoundary;
    private readonly ILogger<DownloadInboundFileCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public DownloadInboundFileCommandHandler(
        WorkItemClaimer claimer,
        InboundFileLander lander,
        IWorkItemFailureHandler failureHandler,
        IFaultBoundary faultBoundary,
        ILogger<DownloadInboundFileCommandHandler> logger)
    {
        _claimer = claimer;
        _lander = lander;
        _failureHandler = failureHandler;
        _faultBoundary = faultBoundary;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkItemDisposition> Handle(DownloadInboundFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workItem = request.WorkItem;
        using var messageScope = _logger.BeginScope(LogScopes.ForWorkItem(InboundWorkStage.Download, workItem));
        var claim = await _claimer.ClaimAsync(InboundWorkStage.Download, workItem, ClaimStateFor, cancellationToken).ConfigureAwait(false);
        return claim.IsClaimed
            ? await LandClaimedAsync(workItem, claim, cancellationToken).ConfigureAwait(false)
            : claim.Disposition;
    }

    private static InboundFileState? ClaimStateFor(InboundFileState state)
        => state is InboundFileState.Discovered or InboundFileState.Queued or InboundFileState.Downloading ? InboundFileState.Downloading : null;

    private async Task<WorkItemDisposition> LandClaimedAsync(InboundFileWorkItem workItem, WorkItemClaim claim, CancellationToken cancellationToken)
    {
        var entry = claim.Entry!;
        using var fileScope = _logger.BeginScope(LogScopes.ForFile(entry.ToRemoteFile()));
        var landed = await _faultBoundary
            .RunAsync(OperationNames.LandInboundFile, () => _lander.LandAsync(claim.ClearingHouse!, entry, cancellationToken))
            .ConfigureAwait(false);

        if (landed.IsSucceeded)
        {
            return await _lander.RecordAsync(entry, landed.Value!, cancellationToken).ConfigureAwait(false);
        }

        var failure = new WorkItemFailure(InboundWorkStage.Download, workItem, entry, InboundFileState.Downloading, InboundFileState.Queued, landed.Exception?.Message);
        return await _failureHandler.HandleAsync(failure, cancellationToken).ConfigureAwait(false);
    }
}

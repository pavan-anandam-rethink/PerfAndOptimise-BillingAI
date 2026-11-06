using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Process worker entry point. A Landed file is claimed into Processing and registered with Billing; a file Billing already
/// queued (BillingQueued) is claimed into Deleting and only the remote delete runs. Does nothing unless the mode is Active.
/// </summary>
public sealed class ProcessLandedFileCommandHandler : IRequestHandler<ProcessLandedFileCommand, WorkItemDisposition>
{
    private readonly WorkItemClaimer _claimer;
    private readonly BillingRegistrationStep _registrationStep;
    private readonly RemoteFileFinalizer _finalizer;
    private readonly PipelineOptions _options;
    private readonly ILogger<ProcessLandedFileCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ProcessLandedFileCommandHandler(
        WorkItemClaimer claimer,
        BillingRegistrationStep registrationStep,
        RemoteFileFinalizer finalizer,
        IOptions<PipelineOptions> options,
        ILogger<ProcessLandedFileCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _claimer = claimer;
        _registrationStep = registrationStep;
        _finalizer = finalizer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkItemDisposition> Handle(ProcessLandedFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workItem = request.WorkItem;
        using var messageScope = _logger.BeginScope(LogScopes.ForWorkItem(InboundWorkStage.Process, workItem));
        if (!_options.IsActive)
        {
            PipelineLog.ProcessingNotActive(_logger, _options.Mode.ToString());
            return WorkItemDisposition.Completed;
        }

        var claim = await _claimer.ClaimAsync(InboundWorkStage.Process, workItem, ClaimStateFor, cancellationToken).ConfigureAwait(false);
        if (!claim.IsClaimed)
        {
            return claim.Disposition;
        }

        using var fileScope = _logger.BeginScope(LogScopes.ForFile(claim.Entry.ToRemoteFile()));
        return claim.Entry.State == InboundFileState.Deleting
            ? await _finalizer.FinishAsync(workItem, claim, cancellationToken).ConfigureAwait(false)
            : await _registrationStep.RunAsync(workItem, claim, cancellationToken).ConfigureAwait(false);
    }

    private static InboundFileState? ClaimStateFor(InboundFileState state) => state switch
    {
        InboundFileState.Landed or InboundFileState.Processing => InboundFileState.Processing,
        InboundFileState.BillingQueued or InboundFileState.Deleting => InboundFileState.Deleting,
        _ => null,
    };
}

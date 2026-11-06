using MediatR;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Resumes a quarantined file from the furthest point it safely reached: after Billing queued it only the delete is
/// repeated; after it landed, processing restarts from the landed copy; otherwise it is downloaded again.
/// </summary>
public sealed class ReplayQuarantinedFileCommandHandler : IRequestHandler<ReplayQuarantinedFileCommand, ReplayResult>
{
    private readonly IInboundFileManifest _manifest;
    private readonly IInboundWorkSender _workSender;
    private readonly ILogger<ReplayQuarantinedFileCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ReplayQuarantinedFileCommandHandler(IInboundFileManifest manifest, IInboundWorkSender workSender, ILogger<ReplayQuarantinedFileCommandHandler> logger)
    {
        _manifest = manifest;
        _workSender = workSender;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReplayResult> Handle(ReplayQuarantinedFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _manifest.FindAsync(request.FileId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new ReplayResult(ReplayOutcome.NotFound, null);
        }

        var resumeState = PipelineStages.ResumeStateFor(entry);
        var reset = InboundFileTransition.Create(entry.Id, resumeState, InboundFileState.Quarantined) with { Attempts = 0 };
        if (!await _manifest.TryTransitionAsync(reset, cancellationToken).ConfigureAwait(false))
        {
            return new ReplayResult(ReplayOutcome.NotQuarantined, entry.State);
        }

        var stage = PipelineStages.StageFor(resumeState)!.Value;
        await _workSender.SendAsync(stage, [InboundFileWorkItem.First(entry)], cancellationToken).ConfigureAwait(false);
        PipelineLog.FileReplayed(_logger, entry.FileName, resumeState.ToString(), stage.ToString());
        return new ReplayResult(ReplayOutcome.Requeued, resumeState);
    }
}

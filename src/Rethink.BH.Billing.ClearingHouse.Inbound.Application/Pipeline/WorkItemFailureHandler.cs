using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Stores the attempt count and reason, then schedules the retry with backoff. After the last attempt the file is
/// quarantined and the message dead-lettered, so a poison file never loops and is visible to operators.
/// </summary>
public sealed class WorkItemFailureHandler : IWorkItemFailureHandler
{
    private readonly IInboundFileManifest _manifest;
    private readonly IInboundWorkSender _workSender;
    private readonly WorkItemRetryPolicy _retryPolicy;
    private readonly IFailureMessageFormatter _failureMessageFormatter;
    private readonly ILogger<WorkItemFailureHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public WorkItemFailureHandler(
        IInboundFileManifest manifest,
        IInboundWorkSender workSender,
        WorkItemRetryPolicy retryPolicy,
        IFailureMessageFormatter failureMessageFormatter,
        ILogger<WorkItemFailureHandler> logger)
    {
        _manifest = manifest;
        _workSender = workSender;
        _retryPolicy = retryPolicy;
        _failureMessageFormatter = failureMessageFormatter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkItemDisposition> HandleAsync(WorkItemFailure failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var attempts = failure.Entry.Attempts + 1;
        var reason = _failureMessageFormatter.Format(failure.Reason);
        var exhausted = _retryPolicy.IsExhausted(attempts);
        var transition = new InboundFileTransition
        {
            FileId = failure.Entry.Id,
            FromStates = [failure.ClaimedState],
            ToState = exhausted ? InboundFileState.Quarantined : failure.RetryState,
            Attempts = attempts,
            LastError = reason,
        };
        await _manifest.TryTransitionAsync(transition, cancellationToken).ConfigureAwait(false);

        if (exhausted)
        {
            PipelineLog.FileQuarantined(_logger, failure.Stage.ToString(), failure.Entry.FileName, attempts, reason);
            return WorkItemDisposition.Quarantined;
        }

        var delay = _retryPolicy.DelayAfter(attempts);
        await _workSender.ScheduleAsync(failure.Stage, failure.WorkItem.NextAttempt(), delay, cancellationToken).ConfigureAwait(false);
        PipelineLog.RetryScheduled(_logger, failure.Stage.ToString(), failure.Entry.FileName, attempts, (long)delay.TotalSeconds, reason);
        return WorkItemDisposition.RetryScheduled;
    }
}

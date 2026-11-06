using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>A failed attempt of one work item.</summary>
/// <param name="Stage">Stage that failed.</param>
/// <param name="WorkItem">Message being handled.</param>
/// <param name="Entry">File as read before the attempt.</param>
/// <param name="ClaimedState">State the handler moved the file into for this attempt.</param>
/// <param name="RetryState">State the file returns to so the retry can claim it again.</param>
/// <param name="Reason">Failure reason; trimmed and truncated before it is stored or logged.</param>
public sealed record WorkItemFailure(
    InboundWorkStage Stage,
    InboundFileWorkItem WorkItem,
    InboundFileEntry Entry,
    InboundFileState ClaimedState,
    InboundFileState RetryState,
    string? Reason);

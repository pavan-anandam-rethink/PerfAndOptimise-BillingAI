namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>How the queue adapter settles a message after the handler ran.</summary>
public enum WorkItemDisposition
{
    /// <summary>Done, or nothing to do (already handled). Complete the message.</summary>
    Completed,

    /// <summary>A delayed retry message was scheduled. Complete this one.</summary>
    RetryScheduled,

    /// <summary>Retries are exhausted. Dead-letter the message.</summary>
    Quarantined,
}

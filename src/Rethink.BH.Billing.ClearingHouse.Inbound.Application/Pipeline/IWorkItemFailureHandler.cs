using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Records a failed attempt and either schedules a delayed retry or quarantines the file.</summary>
public interface IWorkItemFailureHandler
{
    /// <summary>Handles <paramref name="failure"/> and returns how the current message should be settled.</summary>
    Task<WorkItemDisposition> HandleAsync(WorkItemFailure failure, CancellationToken cancellationToken);
}

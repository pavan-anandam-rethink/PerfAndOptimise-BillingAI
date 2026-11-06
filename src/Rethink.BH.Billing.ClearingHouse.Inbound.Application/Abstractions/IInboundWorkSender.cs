using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Sends work to the at-least-once stage queues, one per stage. Handlers must tolerate duplicate deliveries.</summary>
public interface IInboundWorkSender
{
    /// <summary>Sends <paramref name="items"/> to the <paramref name="stage"/> queue now.</summary>
    Task SendAsync(InboundWorkStage stage, IReadOnlyList<InboundFileWorkItem> items, CancellationToken cancellationToken);

    /// <summary>Sends <paramref name="item"/> to the <paramref name="stage"/> queue after <paramref name="delay"/>.</summary>
    Task ScheduleAsync(InboundWorkStage stage, InboundFileWorkItem item, TimeSpan delay, CancellationToken cancellationToken);
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>The parts of a received message the handler needs.</summary>
/// <param name="MessageId">Broker message id, for logs.</param>
/// <param name="Body">Message body.</param>
/// <param name="DeliveryCount">How many times the broker has delivered this message.</param>
/// <param name="EntityPath">Queue name.</param>
public sealed record ReceivedWorkMessage(string MessageId, BinaryData Body, int DeliveryCount, string EntityPath);

/// <summary>Settles the message being handled.</summary>
public interface IWorkMessageSettlement
{
    /// <summary>Removes the message from the queue.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);

    /// <summary>Moves the message to the dead-letter sub-queue.</summary>
    Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken);
}

/// <summary>
/// Handles one queue message: reads the work item, runs the stage command in its own DI scope, and settles the message
/// from the result. If the command throws (for example the manifest database is down), the message is not settled; the
/// processor logs the error, the lock lapses, and the broker redelivers it until the queue's max delivery count.
/// </summary>
public sealed class InboundWorkMessageHandler
{
    /// <summary>Dead-letter reason for a body that is not a work item.</summary>
    public const string InvalidBodyReason = "InvalidBody";

    /// <summary>Dead-letter reason for a file whose retries are exhausted.</summary>
    public const string QuarantinedReason = "Quarantined";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboundWorkMessageHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public InboundWorkMessageHandler(IServiceScopeFactory scopeFactory, ILogger<InboundWorkMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Handles and settles <paramref name="message"/>.</summary>
    public async Task HandleAsync(InboundWorkStage stage, ReceivedWorkMessage message, IWorkMessageSettlement settlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settlement);

        var item = WorkItemSerializer.TryDeserialize(message.Body);
        if (item is null)
        {
            InfrastructureLog.InvalidWorkMessage(_logger, message.MessageId, message.EntityPath);
            await settlement.DeadLetterAsync(InvalidBodyReason, "The message body is not an inbound work item.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var disposition = await DispatchAsync(stage, item, cancellationToken).ConfigureAwait(false);
        if (disposition == WorkItemDisposition.Quarantined)
        {
            await settlement.DeadLetterAsync(QuarantinedReason, "Retries are exhausted; the manifest row holds the last error.", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await settlement.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        InfrastructureLog.WorkMessageSettled(_logger, message.MessageId, message.EntityPath, disposition.ToString(), message.DeliveryCount);
    }

    private async Task<WorkItemDisposition> DispatchAsync(InboundWorkStage stage, InboundFileWorkItem item, CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IInboundWorkDispatcher>();
            return await dispatcher.DispatchAsync(stage, item, cancellationToken).ConfigureAwait(false);
        }
    }
}

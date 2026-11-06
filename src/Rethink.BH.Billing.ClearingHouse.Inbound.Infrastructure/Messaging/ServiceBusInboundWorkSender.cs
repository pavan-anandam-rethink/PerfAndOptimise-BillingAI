using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>
/// Sends work items to the stage queues. Lists are sent in as few size-limited batches as possible; retries use Service
/// Bus scheduled messages, so no pod has to stay alive to wait out a back-off. The file id is the correlation id, so
/// every message for one file can be found in Service Bus Explorer without reading bodies.
/// </summary>
public sealed class ServiceBusInboundWorkSender : IInboundWorkSender, IAsyncDisposable
{
    private readonly ServiceBusClientProvider _clientProvider;
    private readonly ServiceBusOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServiceBusInboundWorkSender> _logger;
    private readonly ConcurrentDictionary<InboundWorkStage, ServiceBusSender> _senders = new();

    /// <summary>Creates the sender.</summary>
    public ServiceBusInboundWorkSender(
        ServiceBusClientProvider clientProvider,
        IOptions<ServiceBusOptions> options,
        TimeProvider timeProvider,
        ILogger<ServiceBusInboundWorkSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clientProvider = clientProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(InboundWorkStage stage, IReadOnlyList<InboundFileWorkItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var sender = await GetSenderAsync(stage, cancellationToken).ConfigureAwait(false);
        var index = 0;
        while (index < items.Count)
        {
            using var batch = await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
            while (index < items.Count && batch.TryAddMessage(ToMessage(stage, items[index])))
            {
                index++;
            }

            if (batch.Count == 0)
            {
                throw new InvalidOperationException($"A work item does not fit in an empty {sender.EntityPath} batch.");
            }

            await sender.SendMessagesAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        InfrastructureLog.WorkMessagesSent(_logger, items.Count, sender.EntityPath, 0);
    }

    /// <inheritdoc />
    public async Task ScheduleAsync(InboundWorkStage stage, InboundFileWorkItem item, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var sender = await GetSenderAsync(stage, cancellationToken).ConfigureAwait(false);
        await sender.ScheduleMessageAsync(ToMessage(stage, item), _timeProvider.GetUtcNow() + delay, cancellationToken).ConfigureAwait(false);
        InfrastructureLog.WorkMessagesSent(_logger, 1, sender.EntityPath, (long)delay.TotalSeconds);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _senders.Clear();
    }

    private static ServiceBusMessage ToMessage(InboundWorkStage stage, InboundFileWorkItem item)
        => new(WorkItemSerializer.Serialize(item))
        {
            ContentType = WorkItemSerializer.ContentType,
            CorrelationId = item.FileId.ToString("N"),
            Subject = stage.ToString(),
        };

    private async ValueTask<ServiceBusSender> GetSenderAsync(InboundWorkStage stage, CancellationToken cancellationToken)
    {
        if (_senders.TryGetValue(stage, out var existing))
        {
            return existing;
        }

        var client = await _clientProvider.GetClientAsync(cancellationToken).ConfigureAwait(false);
        return _senders.GetOrAdd(stage, (key, state) => state.Client.CreateSender(state.Options.QueueFor(key)), (Client: client, Options: _options));
    }
}

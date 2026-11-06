using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>Settings for one stage worker.</summary>
/// <param name="Stage">Stage whose queue is read.</param>
/// <param name="QueueName">Queue name.</param>
/// <param name="MaxConcurrentCalls">Messages handled in parallel by this pod.</param>
/// <param name="MaxAutoLockRenewal">How long a message lock is renewed while it is handled.</param>
public sealed record InboundWorkerSettings(InboundWorkStage Stage, string QueueName, int MaxConcurrentCalls, TimeSpan MaxAutoLockRenewal);

/// <summary>
/// Reads one stage queue with peek-lock and manual settlement. Pods scale on queue length through KEDA; on shutdown the
/// processor stops taking messages and lets the ones in flight finish within the host shutdown timeout.
/// </summary>
public sealed class InboundWorkerHostedService : IHostedService, IAsyncDisposable
{
    private readonly InboundWorkerSettings _settings;
    private readonly ServiceBusClientProvider _clientProvider;
    private readonly InboundWorkMessageHandler _handler;
    private readonly ILogger<InboundWorkerHostedService> _logger;
    private ServiceBusProcessor? _processor;

    /// <summary>Creates the worker.</summary>
    public InboundWorkerHostedService(
        InboundWorkerSettings settings,
        ServiceBusClientProvider clientProvider,
        InboundWorkMessageHandler handler,
        ILogger<InboundWorkerHostedService> logger)
    {
        _settings = settings;
        _clientProvider = clientProvider;
        _handler = handler;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var client = await _clientProvider.GetClientAsync(cancellationToken).ConfigureAwait(false);
        _processor = client.CreateProcessor(_settings.QueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = _settings.MaxConcurrentCalls,
            MaxAutoLockRenewalDuration = _settings.MaxAutoLockRenewal,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        InfrastructureLog.WorkerStateChanged(_logger, _settings.QueueName, "started", _settings.MaxConcurrentCalls);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is null)
        {
            return;
        }

        await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
        InfrastructureLog.WorkerStateChanged(_logger, _settings.QueueName, "stopped", _settings.MaxConcurrentCalls);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }
    }

    private Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var message = new ReceivedWorkMessage(args.Message.MessageId, args.Message.Body, args.Message.DeliveryCount, args.EntityPath);
        return _handler.HandleAsync(_settings.Stage, message, new ProcessorSettlement(args), args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        InfrastructureLog.ServiceBusError(_logger, args.EntityPath, args.ErrorSource.ToString(), args.Exception);
        return Task.CompletedTask;
    }

    private sealed class ProcessorSettlement : IWorkMessageSettlement
    {
        private readonly ProcessMessageEventArgs _args;

        public ProcessorSettlement(ProcessMessageEventArgs args) => _args = args;

        public Task CompleteAsync(CancellationToken cancellationToken) => _args.CompleteMessageAsync(_args.Message, cancellationToken);

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
            => _args.DeadLetterMessageAsync(_args.Message, reason, description, cancellationToken);
    }
}

using System.Reflection;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class MessagingTests
{
    private const string ServiceBusConnection = "Endpoint=sb://sb-test.servicebus.windows.net/;SharedAccessKeyName=send;SharedAccessKey=dGVzdA==";

    private static readonly InboundFileWorkItem Item = new(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"), 1, 2);

    private readonly LogSink _sink = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(TestData.BaseTimeUtc));

    [Fact]
    public void Work_items_round_trip_as_camel_case_ids_only()
    {
        var body = WorkItemSerializer.Serialize(Item);

        body.ToString().Should().Be("{\"fileId\":\"6f9619ff-8b86-d011-b42d-00cf4fc964ff\",\"clearingHouseId\":1,\"attempt\":2}");
        WorkItemSerializer.TryDeserialize(body).Should().Be(Item);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"fileId\":\"00000000-0000-0000-0000-000000000000\",\"clearingHouseId\":1,\"attempt\":0}")]
    [InlineData("{\"fileId\":\"6f9619ff-8b86-d011-b42d-00cf4fc964ff\",\"clearingHouseId\":1,\"attempt\":-1}")]
    public void Bodies_that_are_not_work_items_are_rejected(string json)
    {
        WorkItemSerializer.TryDeserialize(BinaryData.FromString(json)).Should().BeNull();
        FluentActions.Invoking(() => WorkItemSerializer.TryDeserialize(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_map_stages_to_queues()
    {
        var options = new ServiceBusOptions();

        options.QueueFor(InboundWorkStage.Download).Should().Be("inbound-download");
        options.QueueFor(InboundWorkStage.Process).Should().Be("inbound-process");
        options.IsConfigured.Should().BeFalse();
        new ServiceBusOptions { FullyQualifiedNamespace = "sb.servicebus.windows.net" }.IsConfigured.Should().BeTrue();
        new ServiceBusOptions { ConnectionString = "sb-secret" }.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Client_provider_uses_workload_identity_without_a_connection_string()
    {
        await using var provider = new ServiceBusClientProvider(
            Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { FullyQualifiedNamespace = "sb-test.servicebus.windows.net" }),
            Mock.Of<ISecretResolver>(MockBehavior.Strict));

        var client = await provider.GetClientAsync(CancellationToken.None);

        client.FullyQualifiedNamespace.Should().Be("sb-test.servicebus.windows.net");
        (await provider.GetClientAsync(CancellationToken.None)).Should().BeSameAs(client);
    }

    [Fact]
    public async Task Client_provider_resolves_a_connection_string_once_and_retries_after_failure()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .SetupSequence(r => r.ResolveAsync("sb-secret", ServiceBusOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vault down"))
            .ReturnsAsync(ServiceBusConnection);
        await using var provider = new ServiceBusClientProvider(Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { ConnectionString = "sb-secret" }), resolver.Object);

        var failing = async () => await provider.GetClientAsync(CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => provider.GetClientAsync(CancellationToken.None).AsTask()));

        clients.Should().OnlyContain(client => ReferenceEquals(client, clients[0]));
        clients[0].FullyQualifiedNamespace.Should().Be("sb-test.servicebus.windows.net");
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Disposing_an_unused_client_provider_is_safe()
    {
        var provider = new ServiceBusClientProvider(Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions()), Mock.Of<ISecretResolver>());

        await provider.DisposeAsync();

        FluentActions.Invoking(() => new ServiceBusClientProvider(null!, Mock.Of<ISecretResolver>())).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Sender_splits_large_lists_into_batches_and_reuses_the_sender()
    {
        var (client, _, batches) = SenderMocks(maxPerBatch: 2);
        await using var workSender = WorkSender(client);
        var items = Enumerable.Range(0, 5).Select(i => Item with { Attempt = i }).ToArray();

        await workSender.SendAsync(InboundWorkStage.Download, items, CancellationToken.None);
        await workSender.SendAsync(InboundWorkStage.Download, [Item], CancellationToken.None);
        await workSender.SendAsync(InboundWorkStage.Download, [], CancellationToken.None);

        batches.Select(batch => batch.Count).Should().Equal(2, 2, 1, 1);
        var first = batches[0][0];
        first.CorrelationId.Should().Be(Item.FileId.ToString("N"));
        first.Subject.Should().Be("Download");
        first.ContentType.Should().Be("application/json");
        WorkItemSerializer.TryDeserialize(first.Body).Should().Be(Item with { Attempt = 0 });
        client.Verify(c => c.CreateSender("inbound-download"), Times.Once);
        _sink.ByEventId(2026)[0].State["MessageCount"].Should().Be(5);
    }

    [Fact]
    public async Task Message_that_cannot_fit_an_empty_batch_fails_loudly()
    {
        var (client, _, _) = SenderMocks(maxPerBatch: 0);
        await using var workSender = WorkSender(client);

        var act = () => workSender.SendAsync(InboundWorkStage.Process, [Item], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inbound-process*");
    }

    [Fact]
    public async Task Retries_are_scheduled_messages()
    {
        var (client, sender, _) = SenderMocks(maxPerBatch: 10);
        DateTimeOffset? scheduledFor = null;
        sender
            .Setup(s => s.ScheduleMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback((ServiceBusMessage _, DateTimeOffset at, CancellationToken _) => scheduledFor = at)
            .ReturnsAsync(42L);
        var workSender = WorkSender(client);

        await workSender.ScheduleAsync(InboundWorkStage.Process, Item, TimeSpan.FromMinutes(2), CancellationToken.None);
        await workSender.DisposeAsync();

        scheduledFor.Should().Be(_time.GetUtcNow().AddMinutes(2));
        _sink.SingleEvent(2026).State["DelaySeconds"].Should().Be(120L);
        sender.Verify(s => s.DisposeAsync(), Times.Once);
        await FluentActions.Awaiting(() => workSender.ScheduleAsync(InboundWorkStage.Process, null!, TimeSpan.Zero, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => workSender.SendAsync(InboundWorkStage.Process, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(WorkItemDisposition.Completed, false)]
    [InlineData(WorkItemDisposition.RetryScheduled, false)]
    [InlineData(WorkItemDisposition.Quarantined, true)]
    public async Task Handler_settles_from_the_disposition(WorkItemDisposition disposition, bool deadLettered)
    {
        var dispatcher = new Mock<IInboundWorkDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(InboundWorkStage.Process, Item, It.IsAny<CancellationToken>())).ReturnsAsync(disposition);
        var settlement = new FakeSettlement();

        await Handler(dispatcher.Object).HandleAsync(InboundWorkStage.Process, Message(WorkItemSerializer.Serialize(Item)), settlement, CancellationToken.None);

        settlement.Completed.Should().Be(!deadLettered);
        settlement.DeadLetterReason.Should().Be(deadLettered ? InboundWorkMessageHandler.QuarantinedReason : null);
        _sink.SingleEvent(2025).State["Disposition"].Should().Be(disposition.ToString());
    }

    [Fact]
    public async Task Handler_dead_letters_bodies_that_are_not_work_items()
    {
        var dispatcher = new Mock<IInboundWorkDispatcher>(MockBehavior.Strict);
        var settlement = new FakeSettlement();

        await Handler(dispatcher.Object).HandleAsync(InboundWorkStage.Download, Message(BinaryData.FromString("{}")), settlement, CancellationToken.None);

        settlement.DeadLetterReason.Should().Be(InboundWorkMessageHandler.InvalidBodyReason);
        _sink.SingleEvent(2024).State["EntityPath"].Should().Be("inbound-download");
        await FluentActions.Awaiting(() => Handler(dispatcher.Object).HandleAsync(InboundWorkStage.Download, null!, settlement, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => Handler(dispatcher.Object).HandleAsync(InboundWorkStage.Download, Message(BinaryData.Empty), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_failure_leaves_the_message_unsettled_for_redelivery()
    {
        var dispatcher = new Mock<IInboundWorkDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<InboundWorkStage>(), It.IsAny<InboundFileWorkItem>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("sql"));
        var settlement = new FakeSettlement();

        var act = () => Handler(dispatcher.Object).HandleAsync(InboundWorkStage.Download, Message(WorkItemSerializer.Serialize(Item)), settlement, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        settlement.Completed.Should().BeFalse();
        settlement.DeadLetterReason.Should().BeNull();
    }

    [Fact]
    public async Task Worker_reads_its_queue_with_manual_settlement_and_logs_errors()
    {
        var processor = new Mock<ServiceBusProcessor> { CallBase = true };
        ServiceBusProcessorOptions? options = null;
        var client = new Mock<ServiceBusClient>();
        client
            .Setup(c => c.CreateProcessor("inbound-process", It.IsAny<ServiceBusProcessorOptions>()))
            .Callback((string _, ServiceBusProcessorOptions created) => options = created)
            .Returns(processor.Object);
        processor.Setup(p => p.StartProcessingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        processor.Setup(p => p.StopProcessingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        processor.Setup(p => p.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var dispatcher = new Mock<IInboundWorkDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(InboundWorkStage.Process, Item, It.IsAny<CancellationToken>())).ReturnsAsync(WorkItemDisposition.Completed);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver.SetupGet(r => r.EntityPath).Returns("inbound-process");
        await using var provider = ClientProvider(client.Object);
        var worker = new InboundWorkerHostedService(
            new InboundWorkerSettings(InboundWorkStage.Process, "inbound-process", 3, TimeSpan.FromMinutes(5)),
            provider,
            Handler(dispatcher.Object),
            new CapturingLogger<InboundWorkerHostedService>(_sink));

        await worker.StopAsync(CancellationToken.None);
        await worker.StartAsync(CancellationToken.None);
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(body: WorkItemSerializer.Serialize(Item), messageId: "m-1", deliveryCount: 1);
        await RaiseAsync(processor.Object, "OnProcessMessageAsync", new ProcessMessageEventArgs(received, receiver.Object, CancellationToken.None));
        await RaiseAsync(processor.Object, "OnProcessErrorAsync", new ProcessErrorEventArgs(new ServiceBusException("link lost", ServiceBusFailureReason.ServiceCommunicationProblem), ServiceBusErrorSource.Receive, "sb-test.servicebus.windows.net", "inbound-process", "worker", CancellationToken.None));
        await worker.StopAsync(CancellationToken.None);
        await worker.DisposeAsync();
        await worker.DisposeAsync();

        options!.AutoCompleteMessages.Should().BeFalse();
        options.MaxConcurrentCalls.Should().Be(3);
        options.MaxAutoLockRenewalDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.ReceiveMode.Should().Be(ServiceBusReceiveMode.PeekLock);
        receiver.Verify(r => r.CompleteMessageAsync(received, It.IsAny<CancellationToken>()), Times.Once);
        _sink.SingleEvent(2023).State["ErrorSource"].Should().Be("Receive");
        _sink.ByEventId(2027).Select(entry => entry.State["WorkerState"]).Should().Equal("started", "stopped");
    }

    [Fact]
    public async Task Worker_dead_letters_through_the_processor_arguments()
    {
        var receiver = new Mock<ServiceBusReceiver>();
        receiver.SetupGet(r => r.EntityPath).Returns("inbound-download");
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("garbage"), messageId: "m-2");
        var args = new ProcessMessageEventArgs(received, receiver.Object, CancellationToken.None);
        var processor = new Mock<ServiceBusProcessor> { CallBase = true };
        processor.Setup(p => p.StartProcessingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var client = new Mock<ServiceBusClient>();
        client.Setup(c => c.CreateProcessor(It.IsAny<string>(), It.IsAny<ServiceBusProcessorOptions>())).Returns(processor.Object);
        await using var provider = ClientProvider(client.Object);
        var worker = new InboundWorkerHostedService(
            new InboundWorkerSettings(InboundWorkStage.Download, "inbound-download", 1, TimeSpan.FromMinutes(1)),
            provider,
            Handler(Mock.Of<IInboundWorkDispatcher>()),
            new CapturingLogger<InboundWorkerHostedService>(_sink));

        await worker.StartAsync(CancellationToken.None);
        await RaiseAsync(processor.Object, "OnProcessMessageAsync", args);

        receiver.Verify(
            r => r.DeadLetterMessageAsync(received, InboundWorkMessageHandler.InvalidBodyReason, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Task RaiseAsync(ServiceBusProcessor processor, string method, object args)
        => (Task)typeof(ServiceBusProcessor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(processor, [args])!;

    private static ReceivedWorkMessage Message(BinaryData body) => new("m-1", body, 1, "inbound-download");

    private static ServiceBusClientProvider ClientProvider(ServiceBusClient client)
        => new(Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { FullyQualifiedNamespace = "sb-test.servicebus.windows.net" }), Mock.Of<ISecretResolver>(), (_, _) => client);

    private InboundWorkMessageHandler Handler(IInboundWorkDispatcher dispatcher)
    {
        var services = new ServiceCollection().AddSingleton(dispatcher).BuildServiceProvider();
        return new InboundWorkMessageHandler(services.GetRequiredService<IServiceScopeFactory>(), new CapturingLogger<InboundWorkMessageHandler>(_sink));
    }

    private ServiceBusInboundWorkSender WorkSender(Mock<ServiceBusClient> client)
        => new(ClientProvider(client.Object), Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions()), _time, new CapturingLogger<ServiceBusInboundWorkSender>(_sink));

    private static (Mock<ServiceBusClient> Client, Mock<ServiceBusSender> Sender, List<List<ServiceBusMessage>> Batches) SenderMocks(int maxPerBatch)
    {
        var batches = new List<List<ServiceBusMessage>>();
        var queue = string.Empty;
        var sender = new Mock<ServiceBusSender>();
        sender.SetupGet(s => s.EntityPath).Returns(() => queue);
        sender
            .Setup(s => s.CreateMessageBatchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var store = new List<ServiceBusMessage>();
                batches.Add(store);
                return ServiceBusModelFactory.ServiceBusMessageBatch(256 * 1024, store, tryAddCallback: _ => store.Count < maxPerBatch);
            });
        sender.Setup(s => s.SendMessagesAsync(It.IsAny<ServiceBusMessageBatch>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        sender.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var client = new Mock<ServiceBusClient>();
        client
            .Setup(c => c.CreateSender(It.IsAny<string>()))
            .Callback((string name) => queue = name)
            .Returns(sender.Object);
        return (client, sender, batches);
    }

    private sealed class FakeSettlement : IWorkMessageSettlement
    {
        public bool Completed { get; private set; }

        public string? DeadLetterReason { get; private set; }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
        {
            DeadLetterReason = reason;
            return Task.CompletedTask;
        }
    }
}

using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

/// <summary>
/// Parity scenarios ported from the legacy <c>DownloadInboundFilesServiceTests</c>: the run kind, file outcomes, and side
/// effects must be the same as the legacy service for every case.
/// </summary>
public sealed class DownloadInboundFilesCommandHandlerTests
{
    private readonly DownloadPipeline _pipeline;
    private readonly Mock<IClearingHouseCatalog> _catalog = new();
    private readonly Mock<ISftpSessionFactory> _sessionFactory = new();
    private readonly DownloadInboundFilesCommandHandler _handler;

    public DownloadInboundFilesCommandHandlerTests()
        : this(new DownloadPipeline())
    {
    }

    private DownloadInboundFilesCommandHandlerTests(DownloadPipeline pipeline)
    {
        _pipeline = pipeline;
        _catalog.Setup(catalog => catalog.FindById(TestData.Availity.Id)).Returns(TestData.Availity);
        _sessionFactory.Setup(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>())).ReturnsAsync(_pipeline.Session);
        _handler = CreateHandler(_pipeline);
    }

    [Fact]
    public async Task Unknown_clearing_house_is_invalid_and_never_opens_sftp()
    {
        var result = await _handler.Handle(new DownloadInboundFilesCommand(99), CancellationToken.None);

        result.Should().BeEquivalentTo(new { Kind = DownloadRunKind.Invalid, ClearingHouseId = 99 });
        result.Files.Should().BeEmpty();
        _sessionFactory.Verify(factory => factory.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
        _pipeline.Sink.SingleEvent(1001).State["ClearingHouseId"].Should().Be(99);
    }

    [Fact]
    public async Task Empty_folder_is_empty_and_disposes_the_session()
    {
        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Kind.Should().Be(DownloadRunKind.Empty);
        result.Files.Should().BeEmpty();
        _pipeline.Session.IsDisposed.Should().BeTrue();
        _pipeline.Sink.SingleEvent(1004).State["RunKind"].Should().Be("Empty");
    }

    [Fact]
    public async Task All_files_processed_is_completed_in_modified_order()
    {
        _pipeline.Session
            .With(TestData.File("AV_835_20261104_0003.x12", 3), TestData.Sample("AV_835_20261104_0003.x12"))
            .With(TestData.File("AV_999_20261104_0001.x12", 1), TestData.Sample("AV_999_20261104_0001.x12"))
            .With(TestData.File("AV_277CA_20261104_0002.x12", 2), TestData.Sample("AV_277CA_20261104_0002.x12"))
            .With(TestData.File("AV_TA1_20261104_0004.x12", 4), TestData.Sample("AV_TA1_20261104_0004.x12"));

        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Kind.Should().Be(DownloadRunKind.Completed);
        result.FailedCount.Should().Be(0);
        result.Files.Should().HaveCount(4).And.OnlyContain(file => file.Status == InboundFileStatus.RemoteDeleted);
        _pipeline.Backup.Stored.Keys.Should().BeEquivalentTo(_pipeline.Session.Files.Select(file => file.Name));
        _pipeline.Billing.Registered.Should().HaveCount(4);
        _pipeline.Session.Deleted.Should().HaveCount(4);
        var finished = _pipeline.Sink.SingleEvent(1003);
        finished.State["FilesSeen"].Should().Be(4);
        finished.State["Stored"].Should().Be(4);
        finished.State["LeftOnSftp"].Should().Be(0);
    }

    [Fact]
    public async Task Any_failed_file_makes_the_run_failed_with_counts()
    {
        var accepted = TestData.File("ok.x12");
        var rejected = TestData.File("rejected.x12", 1);
        var notQueued = TestData.File("notqueued.x12", 2);
        _pipeline.Session.With(accepted).With(rejected).With(notQueued);
        _pipeline.Billing.Responses[rejected.Name] = BillingRegistration.Rejected("bad envelope");
        _pipeline.Billing.Responses[notQueued.Name] = BillingRegistration.NotQueued(9);

        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Kind.Should().Be(DownloadRunKind.Failed);
        result.Files.Should().HaveCount(3);
        result.FailedCount.Should().Be(2);
        _pipeline.Session.Deleted.Should().ContainSingle().Which.Should().Be(accepted.Name);
        _pipeline.Sink.SingleEvent(1003).State["LeftOnSftp"].Should().Be(2);
    }

    [Fact]
    public async Task Connection_failure_is_failed_with_no_files_and_logged()
    {
        _sessionFactory
            .Setup(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Clearinghouses:Availity:Hosts is not configured."));

        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Should().BeEquivalentTo(new { Kind = DownloadRunKind.Failed, ClearingHouseId = 1, FailedCount = 0 });
        result.Files.Should().BeEmpty();
        var fault = _pipeline.Sink.SingleEvent(1020);
        fault.Level.Should().Be(LogLevel.Error);
        fault.State["OperationName"].Should().Be("downloadEdiDataFromSftp");
        fault.Scopes.Should().ContainKeys("DownloadRunId", "ClearingHouseId", "ClearingHouse");
        fault.Scopes["ClearingHouse"].Should().Be("Availity");
    }

    [Fact]
    public async Task List_failure_is_failed_and_disposes_the_session()
    {
        _pipeline.Session.ListFailure = new IOException("No such file");

        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Kind.Should().Be(DownloadRunKind.Failed);
        _pipeline.Session.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Run_limit_before_listing_is_empty_like_the_legacy_service()
    {
        _sessionFactory
            .Setup(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        result.Kind.Should().Be(DownloadRunKind.Empty);
        _pipeline.Sink.SingleEvent(1021).State["OperationName"].Should().Be("downloadEdiDataFromSftp");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        _sessionFactory
            .Setup(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(caller.Token));

        var act = () => _handler.Handle(new DownloadInboundFilesCommand(1), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Every_log_in_a_run_carries_the_same_run_id()
    {
        _pipeline.Session.With(TestData.File("a.x12")).With(TestData.File("b.x12", 1));

        await _handler.Handle(new DownloadInboundFilesCommand(1), CancellationToken.None);

        var runIds = _pipeline.Sink.Entries
            .Where(entry => entry.EventId.Id is not 1001)
            .Select(entry => entry.Scopes["DownloadRunId"])
            .Distinct()
            .ToArray();
        runIds.Should().ContainSingle().Which.Should().BeOfType<string>().Which.Should().HaveLength(32);
        _pipeline.Sink.SingleEvent(1000).State["ClearingHouseId"].Should().Be(1);
        _pipeline.Sink.SingleEvent(1002).State["ListedCount"].Should().Be(2);
    }

    [Fact]
    public async Task Handler_rejects_null_request_and_options()
    {
        await FluentActions.Invoking(() => _handler.Handle(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new DownloadInboundFilesCommandHandler(
                _catalog.Object,
                _sessionFactory.Object,
                _pipeline.Selector,
                _pipeline.BatchProcessor,
                _pipeline.FaultBoundary,
                _pipeline.Time,
                null!,
                new CapturingLogger<DownloadInboundFilesCommandHandler>()))
            .Should().Throw<ArgumentNullException>();
    }

    private DownloadInboundFilesCommandHandler CreateHandler(DownloadPipeline pipeline)
        => new(
            _catalog.Object,
            _sessionFactory.Object,
            pipeline.Selector,
            pipeline.BatchProcessor,
            pipeline.FaultBoundary,
            pipeline.Time,
            pipeline.Options,
            new CapturingLogger<DownloadInboundFilesCommandHandler>(pipeline.Sink));
}

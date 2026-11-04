using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

public sealed class FaultBoundaryTests
{
    private readonly CapturingLogger<FaultBoundary> _logger = new();
    private readonly FaultBoundary _boundary;

    public FaultBoundaryTests()
    {
        _boundary = new FaultBoundary(_logger);
    }

    [Fact]
    public async Task RunAsync_returns_value_without_logging()
    {
        var result = await _boundary.RunAsync("op", () => Task.FromResult(42));

        result.Kind.Should().Be(GuardedResultKind.Succeeded);
        result.IsSucceeded.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Exception.Should().BeNull();
        _logger.Sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_logs_fault_once_with_operation_and_exception()
    {
        var failure = new InvalidOperationException("boom");

        var result = await _boundary.RunAsync<int>("UploadBackup", () => throw failure);

        result.Kind.Should().Be(GuardedResultKind.Faulted);
        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeSameAs(failure);
        var entry = _logger.Sink.SingleEvent(1020);
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(failure);
        entry.State["OperationName"].Should().Be("UploadBackup");
        entry.State["ExceptionType"].Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task RunAsync_lets_cancellation_propagate()
    {
        var act = () => _boundary.RunAsync<int>("op", () => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunWithRunLimitAsync_returns_value()
    {
        var result = await _boundary.RunWithRunLimitAsync("op", () => Task.FromResult("ok"), CancellationToken.None);

        result.IsSucceeded.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task RunWithRunLimitAsync_reports_run_limit_as_timed_out()
    {
        var result = await _boundary.RunWithRunLimitAsync<int>("ProcessFiles", () => throw new TaskCanceledException(), CancellationToken.None);

        result.Kind.Should().Be(GuardedResultKind.TimedOut);
        result.Exception.Should().BeNull();
        var entry = _logger.Sink.SingleEvent(1021);
        entry.Level.Should().Be(LogLevel.Error);
        entry.State["OperationName"].Should().Be("ProcessFiles");
    }

    [Fact]
    public async Task RunWithRunLimitAsync_rethrows_when_caller_cancelled()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var act = () => _boundary.RunWithRunLimitAsync<int>("op", () => throw new OperationCanceledException(caller.Token), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunWithRunLimitAsync_logs_other_faults()
    {
        var failure = new IOException("socket");

        var result = await _boundary.RunWithRunLimitAsync<int>("downloadEdiDataFromSftp", () => throw failure, CancellationToken.None);

        result.Kind.Should().Be(GuardedResultKind.Faulted);
        result.Exception.Should().BeSameAs(failure);
        _logger.Sink.SingleEvent(1020).State["ExceptionType"].Should().Be(nameof(IOException));
    }

    [Fact]
    public async Task Null_operations_are_rejected()
    {
        await FluentActions.Invoking(() => _boundary.RunAsync<int>("op", null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => _boundary.RunWithRunLimitAsync<int>("op", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }
}

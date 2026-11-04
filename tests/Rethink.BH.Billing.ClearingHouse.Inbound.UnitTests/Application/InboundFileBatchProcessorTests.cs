using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

public sealed class InboundFileBatchProcessorTests
{
    [Fact]
    public async Task Processes_every_file_with_bounded_parallelism()
    {
        var pipeline = new DownloadPipeline(options => options.BatchConcurrency = 3);
        var files = Enumerable.Range(0, 12).Select(index => TestData.File($"f{index:00}.x12", index)).ToArray();
        foreach (var file in files)
        {
            pipeline.Session.With(file);
        }

        pipeline.Session.BeforeDownload = (_, token) => Task.Delay(20, token);

        var outcomes = await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, files, CancellationToken.None, CancellationToken.None);

        outcomes.Should().HaveCount(12);
        outcomes.Should().OnlyContain(outcome => outcome.Status == InboundFileStatus.RemoteDeleted && !outcome.IsFailed);
        pipeline.Session.MaxConcurrentDownloads.Should().BeInRange(1, 3);
        pipeline.Session.Deleted.Should().HaveCount(12);
    }

    [Fact]
    public async Task One_failing_file_does_not_stop_the_others()
    {
        var pipeline = new DownloadPipeline();
        var good = TestData.File("good.x12");
        var bad = TestData.File("bad.x12", 1);
        pipeline.Session.With(good).With(bad);
        pipeline.Session.DownloadFailures[bad.Name] = new IOException("  channel closed  ");

        var outcomes = await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [good, bad], CancellationToken.None, CancellationToken.None);

        outcomes.Should().HaveCount(2);
        outcomes.Should().ContainSingle(outcome => outcome.FileName == good.Name && outcome.Status == InboundFileStatus.RemoteDeleted);
        outcomes.Should().ContainSingle(outcome => outcome.FileName == bad.Name)
            .Which.Should().Be(InboundFileOutcome.Failed(bad.Name, InboundFileStatus.Failed, "channel closed"));
    }

    [Fact]
    public async Task File_fault_is_logged_with_file_scope_and_left_on_sftp_warning()
    {
        var pipeline = new DownloadPipeline();
        var bad = TestData.File("bad.x12", size: 2048);
        pipeline.Session.With(bad);
        pipeline.Backup.Failures[bad.Name] = new InvalidOperationException("blob 503");

        await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [bad], CancellationToken.None, CancellationToken.None);

        var fault = pipeline.Sink.SingleEvent(1020);
        fault.State["OperationName"].Should().Be("ProcessInboundFile");
        fault.Scopes["FileName"].Should().Be("bad.x12");
        fault.Scopes["FileSizeBytes"].Should().Be(2048L);
        var leftOnSftp = pipeline.Sink.SingleEvent(1012);
        leftOnSftp.State["BackupUploaded"].Should().Be(false);
        leftOnSftp.State["BillingUploaded"].Should().Be(false);
    }

    [Fact]
    public async Task Fault_after_backup_reports_backup_progress()
    {
        var pipeline = new DownloadPipeline();
        var file = TestData.File("a.x12");
        pipeline.Session.With(file);
        pipeline.Billing.Failures[file.Name] = new HttpRequestException("billing down");

        var outcomes = await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [file], CancellationToken.None, CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Failure.Should().Be("billing down");
        pipeline.Sink.SingleEvent(1012).State["BackupUploaded"].Should().Be(true);
    }

    [Fact]
    public async Task Run_limit_returns_the_files_finished_so_far()
    {
        var pipeline = new DownloadPipeline(options => options.BatchConcurrency = 1);
        var first = TestData.File("first.x12");
        var second = TestData.File("second.x12", 1);
        pipeline.Session.With(first).With(second);
        using var runLimit = new CancellationTokenSource();
        pipeline.Session.BeforeDownload = async (file, token) =>
        {
            if (file.Name == second.Name)
            {
                await runLimit.CancelAsync();
                token.ThrowIfCancellationRequested();
            }
        };

        var outcomes = await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [first, second], runLimit.Token, CancellationToken.None);

        outcomes.Should().ContainSingle().Which.FileName.Should().Be(first.Name);
        pipeline.Sink.SingleEvent(1021).State["OperationName"].Should().Be("ProcessFiles");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var pipeline = new DownloadPipeline();
        var file = TestData.File("a.x12");
        pipeline.Session.With(file);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var act = () => pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [file], caller.Token, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Empty_batch_returns_no_outcomes()
    {
        var pipeline = new DownloadPipeline();

        var outcomes = await pipeline.BatchProcessor.ProcessAsync(TestData.Availity, pipeline.Session, [], CancellationToken.None, CancellationToken.None);

        outcomes.Should().BeEmpty();
    }
}

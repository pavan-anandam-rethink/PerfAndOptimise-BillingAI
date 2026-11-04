using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

public sealed class SelectionAndFormattingTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(TestData.BaseTimeUtc.AddMinutes(10)));

    [Fact]
    public void Selector_orders_by_modified_time_then_ordinal_name()
    {
        var selector = new RemoteFileSelector(_time, TestData.Options());
        RemoteFile[] listed = [TestData.File("b.x12", 1), TestData.File("B.x12", 1), TestData.File("a.x12", 2), TestData.File("z.x12", 0)];

        var selected = selector.SelectForProcessing(listed);

        selected.Select(file => file.Name).Should().Equal("z.x12", "B.x12", "b.x12", "a.x12");
    }

    [Fact]
    public void Selector_keeps_every_file_when_age_check_is_off()
    {
        var selector = new RemoteFileSelector(_time, TestData.Options());

        selector.SelectForProcessing([TestData.File("new.x12", 10)]).Should().ContainSingle();
    }

    [Fact]
    public void Selector_skips_files_younger_than_the_minimum_age()
    {
        var selector = new RemoteFileSelector(_time, TestData.Options(options => options.MinimumFileAgeSeconds = 120));
        RemoteFile[] listed = [TestData.File("old.x12", 0), TestData.File("edge.x12", 8), TestData.File("writing.x12", 9)];

        var selected = selector.SelectForProcessing(listed);

        selected.Select(file => file.Name).Should().Equal("old.x12", "edge.x12");
    }

    [Fact]
    public void Selector_rejects_null()
    {
        var selector = new RemoteFileSelector(_time, TestData.Options());

        FluentActions.Invoking(() => selector.SelectForProcessing(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new RemoteFileSelector(_time, null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "Download failed.")]
    [InlineData("", "Download failed.")]
    [InlineData("  ", "Download failed.")]
    [InlineData("  time  ", "time")]
    [InlineData("  timeout  ", "timeo")]
    [InlineData("abcdefghij", "abcde")]
    [InlineData("abcde", "abcde")]
    public void Formatter_trims_defaults_and_truncates(string? failure, string expected)
    {
        var formatter = new FailureMessageFormatter(TestData.Options(options => options.FailureMessageMaxLength = 5));

        formatter.Format(failure).Should().Be(expected);
    }

    [Fact]
    public void Formatter_rejects_null_options()
    {
        FluentActions.Invoking(() => new FailureMessageFormatter(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Progress_tracks_backup_and_billing()
    {
        var progress = new InboundFileProgress();
        progress.BackupStored.Should().BeFalse();
        progress.BillingQueued.Should().BeFalse();

        progress.MarkBackupStored();
        progress.MarkBillingQueued();

        progress.BackupStored.Should().BeTrue();
        progress.BillingQueued.Should().BeTrue();
    }

    [Fact]
    public void Options_defaults_match_legacy_appsettings()
    {
        var options = new InboundDownloadOptions();

        options.MaxRunMinutes.Should().Be(13);
        options.BatchConcurrency.Should().Be(2);
        options.SftpPoolSize.Should().Be(4);
        options.ConnectTimeoutSeconds.Should().Be(30);
        options.FailureMessageMaxLength.Should().Be(500);
        options.DeleteRemoteFiles.Should().BeTrue();
        options.MinimumFileAgeSeconds.Should().Be(0);
        options.SftpRetryCount.Should().Be(0);
        options.SftpRetryBaseDelayMilliseconds.Should().Be(500);
        options.MaxRunDuration.Should().Be(TimeSpan.FromMinutes(13));
        InboundDownloadOptions.SectionName.Should().Be("InboundDownload");
    }
}

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

public sealed class DiagnosticsAndRegistrationTests
{
    [Fact]
    public async Task Request_behavior_scopes_and_times_the_request()
    {
        var time = new FakeTimeProvider();
        var logger = new CapturingLogger<RequestLoggingBehavior<DownloadInboundFilesCommand, InboundDownloadResult>>();
        var behavior = new RequestLoggingBehavior<DownloadInboundFilesCommand, InboundDownloadResult>(logger, time);
        var expected = InboundDownloadResult.Empty(1);

        var result = await behavior.Handle(
            new DownloadInboundFilesCommand(1),
            () =>
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
                return Task.FromResult(expected);
            },
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        var started = logger.Sink.SingleEvent(1030);
        started.State["RequestName"].Should().Be(nameof(DownloadInboundFilesCommand));
        started.Scopes["RequestName"].Should().Be(nameof(DownloadInboundFilesCommand));
        logger.Sink.SingleEvent(1031).State["ElapsedMilliseconds"].Should().Be(250L);
    }

    [Fact]
    public async Task Request_behavior_does_not_swallow_exceptions()
    {
        var behavior = new RequestLoggingBehavior<DownloadInboundFilesCommand, InboundDownloadResult>(
            new CapturingLogger<RequestLoggingBehavior<DownloadInboundFilesCommand, InboundDownloadResult>>(),
            TimeProvider.System);

        var act = () => behavior.Handle(new DownloadInboundFilesCommand(1), () => throw new InvalidOperationException("x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => behavior.Handle(new DownloadInboundFilesCommand(1), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Scopes_use_stable_keys()
    {
        LogScopes.ForRun(TestData.Stedi, "run-1").Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["DownloadRunId"] = "run-1",
            ["ClearingHouseId"] = 8,
            ["ClearingHouse"] = "Stedi",
        });
        LogScopes.ForFile(TestData.File("a.x12", size: 10)).Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["FileName"] = "a.x12",
            ["FileSizeBytes"] = 10L,
        });
        LogScopes.ForRequest("Cmd")["RequestName"].Should().Be("Cmd");
        FluentActions.Invoking(() => LogScopes.ForRun(null!, "x")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => LogScopes.ForFile(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Operation_names_are_stable_for_dashboards()
    {
        OperationNames.DownloadEdiDataFromSftp.Should().Be("downloadEdiDataFromSftp");
        OperationNames.ProcessFiles.Should().Be("ProcessFiles");
        OperationNames.ProcessInboundFile.Should().Be("ProcessInboundFile");
        OperationNames.DeleteRemoteFile.Should().Be("DeleteRemoteFile");
    }

    [Fact]
    public void RunCompleted_reports_counts_or_no_files()
    {
        var logger = new CapturingLogger<object>();
        var result = InboundDownloadResult.FromOutcomes(1, [
            InboundFileOutcome.Completed("a", InboundFileStatus.RemoteDeleted),
            InboundFileOutcome.Failed("b", InboundFileStatus.Stored, "x"),
        ]);

        InboundLog.RunCompleted(logger, TestData.Availity, result, 12);
        InboundLog.RunCompleted(logger, TestData.Availity, InboundDownloadResult.Empty(1), 3);

        var finished = logger.Sink.SingleEvent(1003);
        finished.State["RunKind"].Should().Be("Failed");
        finished.State["Stored"].Should().Be(1);
        finished.State["LeftOnSftp"].Should().Be(1);
        logger.Sink.SingleEvent(1004).State["ElapsedMilliseconds"].Should().Be(3L);
        FluentActions.Invoking(() => InboundLog.RunCompleted(logger, null!, result, 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => InboundLog.RunCompleted(logger, TestData.Availity, null!, 0)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddInboundApplication_registers_singletons_and_mediator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["InboundDownload:BatchConcurrency"] = "5" })
            .Build();
        var services = new ServiceCollection().AddLogging();

        services.AddInboundApplication(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        provider.GetRequiredService<IOptions<InboundDownloadOptions>>().Value.BatchConcurrency.Should().Be(5);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<IFaultBoundary>().Should().BeOfType<FaultBoundary>();
        provider.GetRequiredService<IFailureMessageFormatter>().Should().BeSameAs(provider.GetRequiredService<IFailureMessageFormatter>());
        provider.GetRequiredService<IRemoteFileSelector>().Should().BeOfType<RemoteFileSelector>();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IInboundFileBatchProcessor) && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IRequestHandler<DownloadInboundFilesCommand, InboundDownloadResult>));
        services.Should().Contain(descriptor => descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>));
        FluentActions.Invoking(() => services.AddInboundApplication(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Invalid_options_fail_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["InboundDownload:BatchConcurrency"] = "0" })
            .Build();
        using var provider = new ServiceCollection().AddLogging().AddInboundApplication(configuration).BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<InboundDownloadOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("BatchConcurrency");
    }

    [Fact]
    public void Log_levels_match_severity()
    {
        var logger = new CapturingLogger<object>();

        InboundLog.FileLeftOnSftp(logger, "a", true, false);
        InboundLog.OperationTimedOut(logger, "op");

        logger.Sink.SingleEvent(1012).Level.Should().Be(LogLevel.Warning);
        logger.Sink.SingleEvent(1021).Level.Should().Be(LogLevel.Error);
    }
}

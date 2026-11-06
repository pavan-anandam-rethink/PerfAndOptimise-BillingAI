using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Api;

/// <summary>Operator endpoints and process roles on the real host, with the pipeline adapters replaced by in-memory fakes.</summary>
public sealed class PipelineApiTests : IClassFixture<PipelineApiTests.PipelineApiFactory>
{
    private const string AppKey = "22222222-2222-2222-2222-222222222222";
    private const string OperatorKey = "operator-secret";

    private readonly PipelineApiFactory _factory;

    public PipelineApiTests(PipelineApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task On_demand_listing_then_backlog_shows_queued_files()
    {
        _factory.Session.Files.Clear();
        _factory.Session.With(TestData.File($"{Guid.NewGuid():N}.835", size: 10), TestData.Bytes("ISA*api~"));
        using var client = Client(OperatorKey);

        var listing = await client.PostAsync(new Uri("/inbound-pipeline/clearing-houses/1/list", UriKind.Relative), null);
        var backlog = await client.GetFromJsonAsync<PipelineBacklogResponse>(new Uri("/inbound-pipeline/backlog", UriKind.Relative));

        listing.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await listing.Content.ReadFromJsonAsync<ListingResponse>();
        body!.Outcome.Should().Be("Completed");
        body.Enqueued.Should().Be(1);
        backlog!.CountsByState["Queued"].Should().BeGreaterThanOrEqualTo(1);
        backlog.OldestPendingDiscoveredUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Quarantined_files_are_listed_without_names_and_can_be_replayed()
    {
        var entry = await QuarantinedEntryAsync();
        using var client = Client(OperatorKey);

        var quarantined = await client.GetStringAsync(new Uri("/inbound-pipeline/quarantined?maxCount=500", UriKind.Relative));
        var replay = await client.PostAsync(new Uri($"/inbound-pipeline/files/{entry.Id}/replay", UriKind.Relative), null);
        var again = await client.PostAsync(new Uri($"/inbound-pipeline/files/{entry.Id}/replay", UriKind.Relative), null);
        var missing = await client.PostAsync(new Uri($"/inbound-pipeline/files/{Guid.NewGuid()}/replay", UriKind.Relative), null);

        quarantined.Should().Contain(entry.Id.ToString()).And.NotContain(entry.FileName).And.NotContain("boom");
        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await replay.Content.ReadFromJsonAsync<ReplayResponse>()).Should().Be(new ReplayResponse(entry.Id, "Requeued", "Queued"));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Wrong_or_missing_operator_key_is_unauthorized()
    {
        using var wrong = Client("nope");
        using var missing = Client(null);

        (await wrong.GetAsync(new Uri("/inbound-pipeline/backlog", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await missing.GetAsync(new Uri("/inbound-pipeline/backlog", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Sink.ByEventId(3032).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Operator_endpoints_do_not_exist_while_the_pipeline_is_off_or_no_key_is_set()
    {
        await using var off = _factory.WithWebHostBuilder(builder => Override(builder, "Pipeline:Mode", "Off"));
        await using var keyless = _factory.WithWebHostBuilder(builder => Override(builder, PipelineHostSettings.OperatorKeyKey, string.Empty));
        using var offClient = off.CreateClient();
        using var keylessClient = keyless.CreateClient();
        foreach (var client in new[] { offClient, keylessClient })
        {
            client.DefaultRequestHeaders.Add("x-application-key", AppKey);
            client.DefaultRequestHeaders.Add("x-operator-key", OperatorKey);
        }

        (await offClient.GetAsync(new Uri("/inbound-pipeline/backlog", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await keylessClient.GetAsync(new Uri("/inbound-pipeline/backlog", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Legacy_endpoint_is_unchanged_while_the_pipeline_is_active()
    {
        _factory.Session.Files.Clear();
        using var client = Client(null);

        var response = await client.PostAsJsonAsync("/ClearingHouseClaimProcessor/downloadEdiDataFromSftp", 1);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(PipelineRole.Api, 0)]
    [InlineData(PipelineRole.Lister, 1)]
    [InlineData(PipelineRole.DownloadWorker, 1)]
    [InlineData(PipelineRole.ProcessWorker, 1)]
    public void Role_selects_the_background_work(PipelineRole role, int hostedServices)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PipelineHostSettings.RoleKey] = role.ToString() }).Build();
        var services = new ServiceCollection();

        services.AddPipelineRole(configuration);

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(hostedServices);
        PipelineHostSettings.Role(configuration).Should().Be(role);
        PipelineHostSettings.Role(new ConfigurationBuilder().Build()).Should().Be(PipelineRole.Api);
        FluentActions.Invoking(() => PipelineHostSettings.Role(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(InboundListingKind.Completed, 0)]
    [InlineData(InboundListingKind.LeaseHeld, 0)]
    [InlineData(InboundListingKind.Disabled, 0)]
    [InlineData(InboundListingKind.Failed, 1)]
    [InlineData(InboundListingKind.Invalid, 1)]
    public void Lister_exit_code_reports_failed_or_unknown_clearing_houses(InboundListingKind kind, int exitCode)
    {
        ListerRunHostedService.ExitCodeFor([InboundListingResult.NotRun(1, InboundListingKind.Completed), InboundListingResult.NotRun(8, kind)]).Should().Be(exitCode);
        FluentActions.Invoking(() => ListerRunHostedService.ExitCodeFor(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Lister_run_lists_every_clearing_house_then_stops_the_host()
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<ListInboundFilesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListInboundFilesCommand command, CancellationToken _) => InboundListingResult.NotRun(command.ClearingHouseId, command.ClearingHouseId == 8 ? InboundListingKind.Failed : InboundListingKind.Completed));
        await using var provider = new ServiceCollection().AddSingleton(sender.Object).BuildServiceProvider();
        var lifetime = new Mock<IHostApplicationLifetime>();
        var sink = new LogSink();
        var options = Options.Create(new PipelineOptions { ClearingHouseIds = [1, 8] });
        var service = new ListerRunHostedService(provider.GetRequiredService<IServiceScopeFactory>(), options, lifetime.Object, new CapturingLogger<ListerRunHostedService>(sink));
        var previous = Environment.ExitCode;

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
        var exitCode = Environment.ExitCode;
        Environment.ExitCode = previous;

        exitCode.Should().Be(1);
        sender.Verify(s => s.Send(It.IsAny<ListInboundFilesCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        lifetime.Verify(l => l.StopApplication(), Times.Once);
        sink.SingleEvent(3030).State["FailedCount"].Should().Be(1);
        await FluentActions.Awaiting(() => ListerRunHostedService.ListAllAsync(null!, [], CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ListerRunHostedService.ListAllAsync(sender.Object, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new ListerRunHostedService(provider.GetRequiredService<IServiceScopeFactory>(), null!, lifetime.Object, new CapturingLogger<ListerRunHostedService>())).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Response_mapper_rejects_nulls()
    {
        FluentActions.Invoking(() => Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers.PipelineResponseMapper.ToResponse((PipelineBacklog)null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers.PipelineResponseMapper.ToResponse((InboundListingResult)null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers.PipelineResponseMapper.ToResponse(Guid.Empty, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers.PipelineResponseMapper.ToResponses(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters.OperatorKeyFilter.KeysMatch("a", null!)).Invoke().Should().BeFalse();
    }

    private static void Override(IWebHostBuilder builder, string key, string value)
        => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }));

    private async Task<InboundFileEntry> QuarantinedEntryAsync()
    {
        var manifest = _factory.Services.GetRequiredService<IInboundFileManifest>();
        var file = TestData.File($"{Guid.NewGuid():N}.835");
        var entry = (await manifest.RegisterAsync(1, [file], CancellationToken.None))[0];
        await manifest.TryTransitionAsync(InboundFileTransition.Create(entry.Id, InboundFileState.Quarantined, InboundFileState.Discovered) with { LastError = "boom", Attempts = 5 }, CancellationToken.None);
        return entry;
    }

    private HttpClient Client(string? operatorKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-application-key", AppKey);
        if (operatorKey is not null)
        {
            client.DefaultRequestHeaders.Add("x-operator-key", operatorKey);
        }

        return client;
    }

    public sealed class PipelineApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteManifestDbContextFactory _database = new();

        public FakeSftpSession Session { get; } = new();

        public LogSink Sink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = PipelineRegistrationTests.ActiveSettings();
                values["KeyVaultUri"] = string.Empty;
                values["Pipeline:MinimumFileAgeSeconds"] = "0";
                values[PipelineHostSettings.OperatorKeyKey] = OperatorKey;
                values["ApplicationSettings:ApplicationKeySettings:AcceptedApplicationKeys:0"] = AppKey;
                values["ApplicationSettings:ApplicationKeySettings:CheckAcceptedApplicationKeys"] = "true";
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                var time = new FakeTimeProvider(new DateTimeOffset(TestData.BaseTimeUtc.AddHours(1)));
                var sessionFactory = new Mock<ISftpSessionFactory>();
                sessionFactory.Setup(f => f.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>())).ReturnsAsync(Session);
                services.RemoveAll<ISftpSessionFactory>();
                services.RemoveAll<IManifestDbContextFactory>();
                services.RemoveAll<IInboundWorkSender>();
                services.RemoveAll<ILandingFileStore>();
                services.RemoveAll<IRunLeaseProvider>();
                services.RemoveAll<ServiceBusInboundWorkSender>();
                services.AddSingleton(sessionFactory.Object);
                services.AddSingleton<IManifestDbContextFactory>(_database);
                services.AddSingleton<IInboundWorkSender>(new FakeWorkSender(time));
                services.AddSingleton<ILandingFileStore>(new FakeLandingStore());
                services.AddSingleton<IRunLeaseProvider>(new FakeRunLeaseProvider());
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new SinkLoggerProvider(Sink));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _database.Dispose();
        }

        private sealed class SinkLoggerProvider(LogSink sink) : Microsoft.Extensions.Logging.ILoggerProvider
        {
            public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger<object>(sink);

            public void Dispose()
            {
            }
        }
    }
}

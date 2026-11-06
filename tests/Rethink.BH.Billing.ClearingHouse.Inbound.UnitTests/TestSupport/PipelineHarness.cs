using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

/// <summary>
/// The real application pipeline and the real SQL manifest (on SQLite) wired over in-memory SFTP, landing, queue, lease,
/// backup, and Billing fakes. Tests list, then pump queue messages exactly as the Service Bus workers would.
/// </summary>
public sealed class PipelineHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public PipelineHarness(PipelineMode mode = PipelineMode.Active, IDictionary<string, string?>? settings = null)
    {
        Queue = new FakeWorkSender(Time);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Pipeline:Mode"] = mode.ToString(),
            ["Pipeline:ClearingHouseIds:0"] = TestData.Availity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Pipeline:MinimumFileAgeSeconds"] = "0",
            ["Pipeline:MaxAttempts"] = "3",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new SinkLoggerFactory(Sink));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<TimeProvider>(Time);
        services.AddInboundApplication(configuration);

        var catalog = new Mock<IClearingHouseCatalog>();
        catalog.Setup(c => c.FindById(TestData.Availity.Id)).Returns(TestData.Availity);
        var sessionFactory = new Mock<ISftpSessionFactory>();
        sessionFactory.Setup(f => f.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>())).ReturnsAsync(Session);
        SessionFactory = sessionFactory;

        services.AddSingleton(catalog.Object);
        services.AddSingleton(sessionFactory.Object);
        services.AddSingleton<IBackupFileStore>(Backup);
        services.AddSingleton<IBillingFileRegistrar>(Billing);
        services.AddSingleton<ILandingFileStore>(Landing);
        services.AddSingleton<IInboundWorkSender>(Queue);
        services.AddSingleton<IRunLeaseProvider>(Leases);
        services.AddSingleton<IManifestDbContextFactory>(Database);
        services.AddSingleton<IInboundFileManifest, SqlInboundFileManifest>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public LogSink Sink { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(TestData.BaseTimeUtc.AddHours(1)));

    public FakeSftpSession Session { get; } = new();

    public Mock<ISftpSessionFactory> SessionFactory { get; }

    public FakeBackupStore Backup { get; } = new();

    public FakeBillingRegistrar Billing { get; } = new();

    public FakeLandingStore Landing { get; } = new();

    public FakeWorkSender Queue { get; }

    public FakeRunLeaseProvider Leases { get; } = new();

    public SqliteManifestDbContextFactory Database { get; } = new();

    public IInboundFileManifest Manifest => _services.GetRequiredService<IInboundFileManifest>();

    public List<(InboundWorkStage Stage, InboundFileWorkItem Item, WorkItemDisposition Disposition)> Handled { get; } = [];

    public T Get<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
        => _services.GetRequiredService<ISender>().Send(request);

    public Task<InboundListingResult> ListAsync(int clearingHouseId = 1) => SendAsync(new ListInboundFilesCommand(clearingHouseId));

    /// <summary>Handles every due message, including ones produced while pumping. Returns how many were handled.</summary>
    public async Task<int> PumpAsync()
    {
        var handled = 0;
        while (Queue.TryTakeDue(out var message))
        {
            await using var scope = _services.CreateAsyncScope();
            var disposition = await scope.ServiceProvider.GetRequiredService<IInboundWorkDispatcher>()
                .DispatchAsync(message.Stage, message.Item, CancellationToken.None);
            Handled.Add((message.Stage, message.Item, disposition));
            handled++;
        }

        return handled;
    }

    /// <summary>Moves the clock and pumps whatever became due.</summary>
    public Task<int> AdvanceAndPumpAsync(TimeSpan by)
    {
        Time.Advance(by);
        return PumpAsync();
    }

    public async Task<InboundFileEntry> SingleEntryAsync()
    {
        var entries = await AllEntriesAsync();
        return entries.Should().ContainSingle().Subject;
    }

    public async Task<IReadOnlyList<InboundFileEntry>> AllEntriesAsync()
    {
        await using var context = Database.CreateContext();
        return context.Files.AsEnumerable().Select(InboundFileRecordMapper.ToEntry).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        Database.Dispose();
    }

    private sealed class SinkLoggerFactory(LogSink sink) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger<object>(sink);

        public void Dispose()
        {
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class ManifestTests : IDisposable
{
    private const string SqlConnection = "Server=tcp:sql.example.net;Database=inbound;Authentication=Active Directory Default;";

    private readonly SqliteManifestDbContextFactory _database = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(TestData.BaseTimeUtc));
    private readonly LogSink _sink = new();
    private readonly SqlInboundFileManifest _manifest;

    public ManifestTests()
    {
        _manifest = new SqlInboundFileManifest(_database, _time, new CapturingLogger<SqlInboundFileManifest>(_sink));
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Register_adds_new_versions_and_returns_only_unqueued_rows()
    {
        var first = await _manifest.RegisterAsync(1, [TestData.File("a.835"), TestData.File("b.835")], CancellationToken.None);
        await _manifest.MarkQueuedAsync([first[0].Id], CancellationToken.None);

        var second = await _manifest.RegisterAsync(1, [TestData.File("a.835"), TestData.File("b.835"), TestData.File("a.835", 5, 256)], CancellationToken.None);

        first.Should().HaveCount(2).And.OnlyContain(entry => entry.State == InboundFileState.Discovered && entry.ClearingHouseId == 1);
        second.Select(entry => (entry.FileName, entry.SizeBytes)).Should().BeEquivalentTo([("b.835", 128L), ("a.835", 256L)]);
        second.Should().Contain(entry => entry.Id == first[1].Id);
        var registered = _sink.ByEventId(2020);
        registered[^1].State["NewCount"].Should().Be(1);
        registered[^1].State["UnqueuedCount"].Should().Be(1);
    }

    [Fact]
    public async Task Register_ignores_the_same_version_listed_twice_in_one_call()
    {
        var entries = await _manifest.RegisterAsync(1, [TestData.File("a.835"), TestData.File("a.835")], CancellationToken.None);

        entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Register_of_nothing_does_not_touch_the_database()
    {
        _database.Failure = new InvalidOperationException("must not connect");

        var entries = await _manifest.RegisterAsync(1, [], CancellationToken.None);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Clearing_houses_are_isolated()
    {
        await _manifest.RegisterAsync(1, [TestData.File("a.835")], CancellationToken.None);

        var other = await _manifest.RegisterAsync(8, [TestData.File("a.835")], CancellationToken.None);

        other.Should().ContainSingle().Which.ClearingHouseId.Should().Be(8);
    }

    [Fact]
    public async Task Transition_is_compare_and_set_and_coalesces_optional_values()
    {
        var entry = (await _manifest.RegisterAsync(1, [TestData.File("a.835")], CancellationToken.None))[0];
        _time.Advance(TimeSpan.FromMinutes(1));

        var wrongState = await _manifest.TryTransitionAsync(InboundFileTransition.Create(entry.Id, InboundFileState.Landed, InboundFileState.Downloading), CancellationToken.None);
        var landed = await _manifest.TryTransitionAsync(
            InboundFileTransition.Create(entry.Id, InboundFileState.Landed, InboundFileState.Discovered) with { Sha256 = new string('A', 64), LandingBlobName = "1/x", Attempts = 2, LastError = new string('e', 2000) },
            CancellationToken.None);
        var keepsValues = await _manifest.TryTransitionAsync(InboundFileTransition.Create(entry.Id, InboundFileState.Processing, InboundFileState.Landed), CancellationToken.None);

        wrongState.Should().BeFalse();
        landed.Should().BeTrue();
        keepsValues.Should().BeTrue();
        var stored = await _manifest.FindAsync(entry.Id, CancellationToken.None);
        stored!.State.Should().Be(InboundFileState.Processing);
        stored.Sha256.Should().Be(new string('A', 64));
        stored.LandingBlobName.Should().Be("1/x");
        stored.Attempts.Should().Be(2);
        stored.LastError.Should().HaveLength(InboundManifestDbContext.LastErrorMaxLength);
        stored.UpdatedUtc.Should().Be(TestData.BaseTimeUtc.AddMinutes(1));
        stored.UpdatedUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Reclaim_succeeds_only_after_the_claim_expires()
    {
        var entry = (await _manifest.RegisterAsync(1, [TestData.File("a.835")], CancellationToken.None))[0];
        var claimUntil = TestData.BaseTimeUtc.AddMinutes(30);
        await _manifest.TryTransitionAsync(InboundFileTransition.Create(entry.Id, InboundFileState.Downloading, InboundFileState.Discovered) with { ClaimExpiresUtc = claimUntil }, CancellationToken.None);
        var reclaim = new InboundFileTransition { FileId = entry.Id, FromStates = [], ToState = InboundFileState.Downloading };

        var early = await _manifest.TryTransitionAsync(reclaim with { ReclaimExpiredBeforeUtc = claimUntil.AddMinutes(-1) }, CancellationToken.None);
        var late = await _manifest.TryTransitionAsync(reclaim with { ReclaimExpiredBeforeUtc = claimUntil.AddMinutes(1), ClaimExpiresUtc = claimUntil.AddHours(1) }, CancellationToken.None);
        var noReclaim = await _manifest.TryTransitionAsync(reclaim, CancellationToken.None);

        early.Should().BeFalse();
        late.Should().BeTrue();
        noReclaim.Should().BeFalse();
        (await _manifest.FindAsync(entry.Id, CancellationToken.None))!.ClaimExpiresUtc.Should().Be(claimUntil.AddHours(1));
    }

    [Fact]
    public async Task Mark_queued_moves_only_discovered_rows()
    {
        var entries = await _manifest.RegisterAsync(1, [TestData.File("a.835"), TestData.File("b.835")], CancellationToken.None);
        await _manifest.TryTransitionAsync(InboundFileTransition.Create(entries[1].Id, InboundFileState.Quarantined, InboundFileState.Discovered), CancellationToken.None);

        await _manifest.MarkQueuedAsync(entries.Select(entry => entry.Id).ToArray(), CancellationToken.None);

        (await _manifest.FindAsync(entries[0].Id, CancellationToken.None))!.State.Should().Be(InboundFileState.Queued);
        (await _manifest.FindAsync(entries[1].Id, CancellationToken.None))!.State.Should().Be(InboundFileState.Quarantined);
        (await _manifest.FindAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Stalled_query_filters_by_clearing_house_state_and_age()
    {
        var old = (await _manifest.RegisterAsync(1, [TestData.File("old.835")], CancellationToken.None))[0];
        await _manifest.RegisterAsync(8, [TestData.File("other.835")], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(3));
        await _manifest.RegisterAsync(1, [TestData.File("new.835")], CancellationToken.None);

        var stalled = await _manifest.ListStalledAsync(1, [InboundFileState.Discovered], TestData.BaseTimeUtc.AddHours(1), 10, CancellationToken.None);
        var limited = await _manifest.ListStalledAsync(1, [InboundFileState.Discovered], TestData.BaseTimeUtc.AddHours(4), 1, CancellationToken.None);
        var wrongState = await _manifest.ListStalledAsync(1, [InboundFileState.Landed], TestData.BaseTimeUtc.AddHours(4), 10, CancellationToken.None);

        stalled.Should().ContainSingle().Which.Id.Should().Be(old.Id);
        limited.Should().ContainSingle().Which.Id.Should().Be(old.Id);
        wrongState.Should().BeEmpty();
    }

    [Fact]
    public async Task List_by_state_is_oldest_first_and_limited()
    {
        await _manifest.RegisterAsync(1, [TestData.File("a.835")], CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _manifest.RegisterAsync(1, [TestData.File("b.835")], CancellationToken.None);

        var page = await _manifest.ListByStateAsync(InboundFileState.Discovered, 1, CancellationToken.None);

        page.Should().ContainSingle().Which.FileName.Should().Be("a.835");
    }

    [Fact]
    public async Task Empty_backlog_has_zero_counts_and_no_oldest_file()
    {
        var backlog = await _manifest.GetBacklogAsync(CancellationToken.None);

        backlog.CountsByState.Should().HaveCount(Enum.GetValues<InboundFileState>().Length).And.OnlyContain(pair => pair.Value == 0);
        backlog.OldestPendingDiscoveredUtc.Should().BeNull();
    }

    [Fact]
    public void Mapper_marks_values_as_utc_and_keys_versions_by_ticks()
    {
        var record = InboundFileRecordMapper.NewRecord(1, TestData.File("a.835") with { ModifiedAtUtc = DateTime.SpecifyKind(TestData.BaseTimeUtc, DateTimeKind.Unspecified) }, TestData.BaseTimeUtc);
        record.ClaimExpiresUtc = DateTime.SpecifyKind(TestData.BaseTimeUtc, DateTimeKind.Unspecified);

        var entry = InboundFileRecordMapper.ToEntry(record);

        entry.RemoteModifiedUtc.Kind.Should().Be(DateTimeKind.Utc);
        entry.ClaimExpiresUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        entry.BillingQueuedUtc.Should().BeNull();
        InboundFileRecordMapper.VersionKey("/a", 1, TestData.BaseTimeUtc).Should().Be(("/a", 1L, TestData.BaseTimeUtc.Ticks));
        FluentActions.Invoking(() => InboundFileRecordMapper.ToEntry(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => InboundFileRecordMapper.NewRecord(1, null!, TestData.BaseTimeUtc)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sql_server_model_uses_the_inbound_schema_and_the_version_index()
    {
        using var context = new InboundManifestDbContext(SqlManifestDbContextFactory.SqlServerOptions(SqlConnection, new ManifestStorageOptions()));

        var entity = context.Model.FindEntityType(typeof(InboundFileRecord))!;

        entity.GetSchema().Should().Be("inbound");
        entity.GetTableName().Should().Be("InboundFile");
        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.GetDatabaseName() == "UX_InboundFile_RemoteVersion");
    }

    [Fact]
    public void Deployment_schema_script_matches_the_model()
    {
        using var context = new InboundManifestDbContext(SqlManifestDbContextFactory.SqlServerOptions(SqlConnection, new ManifestStorageOptions()));
        var expected = Normalize(context.Database.GenerateCreateScript());
        var scriptPath = Path.Combine(RepositoryRoot(), "deployment", "sql", "inbound-manifest.sql");

        var actual = File.Exists(scriptPath) ? ScriptBody(File.ReadAllText(scriptPath)) : string.Empty;
        if (actual != expected)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "inbound-manifest.generated.sql"), context.Database.GenerateCreateScript());
        }

        actual.Should().Be(expected, "deployment/sql/inbound-manifest.sql must be regenerated when the manifest model changes");
    }

    [Fact]
    public async Task Sql_factory_resolves_the_connection_once_and_reuses_the_options()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync("manifest-secret", ManifestStorageOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SqlConnection);
        using var factory = new SqlManifestDbContextFactory(Microsoft.Extensions.Options.Options.Create(new ManifestStorageOptions { ConnectionString = "manifest-secret" }), resolver.Object);

        await using var first = await factory.CreateAsync(CancellationToken.None);
        await using var second = await factory.CreateAsync(CancellationToken.None);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.CreateAsync(CancellationToken.None)));

        first.Database.IsSqlServer().Should().BeTrue();
        first.Database.GetConnectionString().Should().Be(SqlConnection);
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
        foreach (var context in concurrent)
        {
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sql_factory_does_not_cache_a_failed_resolution()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .SetupSequence(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vault down"))
            .ReturnsAsync(SqlConnection);
        using var factory = new SqlManifestDbContextFactory(Microsoft.Extensions.Options.Options.Create(new ManifestStorageOptions { ConnectionString = "manifest-secret" }), resolver.Object);

        var failed = () => factory.CreateAsync(CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        await using var context = await factory.CreateAsync(CancellationToken.None);

        context.Should().NotBeNull();
    }

    [Fact]
    public async Task Commands_and_queries_reject_null_arguments()
    {
        await using var context = _database.CreateContext();
        var transition = InboundFileTransition.Create(Guid.NewGuid(), InboundFileState.Done);

        await FluentActions.Awaiting(() => ManifestTransitionCommand.ExecuteAsync(null!, transition, DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestTransitionCommand.ExecuteAsync(context, null!, DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestTransitionCommand.MarkQueuedAsync(null!, [], DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestTransitionCommand.MarkQueuedAsync(context, null!, DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestQueries.FindAsync(null!, Guid.Empty, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestQueries.CountByStateAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestQueries.OldestPendingAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestRegistration.StageAsync(null!, 1, [], DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => ManifestRegistration.StageAsync(context, 1, null!, DateTime.UtcNow, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => _manifest.RegisterAsync(1, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => ManifestQueries.InState(null!, InboundFileState.Done, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ManifestQueries.Stalled(null!, 1, [], DateTime.UtcNow, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ManifestQueries.Stalled(context, 1, null!, DateTime.UtcNow, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ManifestQueries.AtPaths(null!, 1, [])).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => SqlManifestDbContextFactory.SqlServerOptions(SqlConnection, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new SqlManifestDbContextFactory(null!, Mock.Of<ISecretResolver>())).Should().Throw<ArgumentNullException>();
    }

    private static string ScriptBody(string script)
    {
        const string marker = "-- <generated>";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        return Normalize(start < 0 ? script : script[(start + marker.Length)..]);
    }

    private static string Normalize(string script) => script.ReplaceLineEndings("\n").Trim();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>
/// Builds SQL Server context options once, after resolving the connection string, and reuses them for every context.
/// Transient SQL faults are retried by the EF Core execution strategy. A failed resolution is not cached.
/// </summary>
public sealed class SqlManifestDbContextFactory : IManifestDbContextFactory, IDisposable
{
    private readonly ManifestStorageOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private DbContextOptions<InboundManifestDbContext>? _contextOptions;

    /// <summary>Creates the factory.</summary>
    public SqlManifestDbContextFactory(IOptions<ManifestStorageOptions> options, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public async Task<InboundManifestDbContext> CreateAsync(CancellationToken cancellationToken)
    {
        var contextOptions = Volatile.Read(ref _contextOptions) ?? await BuildOptionsAsync(cancellationToken).ConfigureAwait(false);
        return new InboundManifestDbContext(contextOptions);
    }

    /// <inheritdoc />
    public void Dispose() => _creationLock.Dispose();

    /// <summary>SQL Server options for <paramref name="connectionString"/> with the configured timeout and retries.</summary>
    public static DbContextOptions<InboundManifestDbContext> SqlServerOptions(string connectionString, ManifestStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DbContextOptionsBuilder<InboundManifestDbContext>()
            .UseSqlServer(connectionString, sql => sql
                .CommandTimeout(options.CommandTimeoutSeconds)
                .EnableRetryOnFailure(options.MaxRetryCount))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
    }

    private async Task<DbContextOptions<InboundManifestDbContext>> BuildOptionsAsync(CancellationToken cancellationToken)
    {
        await _creationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var release = new SemaphoreRelease(_creationLock);
        if (_contextOptions is not null)
        {
            return _contextOptions;
        }

        var connectionString = await _secretResolver
            .ResolveAsync(_options.ConnectionString, ManifestStorageOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, cancellationToken)
            .ConfigureAwait(false);

        var built = SqlServerOptions(connectionString, _options);
        Volatile.Write(ref _contextOptions, built);
        return built;
    }

    private sealed class SemaphoreRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public SemaphoreRelease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}

using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>
/// Manifest on SQL Server through EF Core. Every state change is a single <c>UPDATE … WHERE Id = @id AND State IN (…)</c>,
/// so the compare-and-set is atomic without transactions or row locks held across network calls.
/// </summary>
public sealed class SqlInboundFileManifest : IInboundFileManifest
{
    private readonly IManifestDbContextFactory _contextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlInboundFileManifest> _logger;

    /// <summary>Creates the manifest.</summary>
    public SqlInboundFileManifest(IManifestDbContextFactory contextFactory, TimeProvider timeProvider, ILogger<SqlInboundFileManifest> logger)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundFileEntry>> RegisterAsync(int clearingHouseId, IReadOnlyList<RemoteFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            return [];
        }

        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var (pending, added) = await ManifestRegistration.StageAsync(context, clearingHouseId, files, UtcNow(), cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            InfrastructureLog.ManifestRegistered(_logger, files.Count, added, pending.Count - added);
            return pending.ConvertAll(InboundFileRecordMapper.ToEntry);
        }
    }

    /// <inheritdoc />
    public async Task MarkQueuedAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await ManifestTransitionCommand.MarkQueuedAsync(context, fileIds, UtcNow(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<InboundFileEntry?> FindAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await ManifestQueries.FindAsync(context, fileId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryTransitionAsync(InboundFileTransition transition, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var rows = await ManifestTransitionCommand.ExecuteAsync(context, transition, UtcNow(), cancellationToken).ConfigureAwait(false);
            return rows == 1;
        }
    }

    /// <inheritdoc />
    public async Task<PipelineBacklog> GetBacklogAsync(CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var byState = await ManifestQueries.CountByStateAsync(context, cancellationToken).ConfigureAwait(false);
            var oldestPending = await ManifestQueries.OldestPendingAsync(context, cancellationToken).ConfigureAwait(false);
            return new PipelineBacklog(byState, oldestPending);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundFileEntry>> ListByStateAsync(InboundFileState state, int maxCount, CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await ManifestQueries.ToEntriesAsync(ManifestQueries.InState(context, state, maxCount), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundFileEntry>> ListStalledAsync(
        int clearingHouseId,
        IReadOnlyCollection<InboundFileState> states,
        DateTime updatedBeforeUtc,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = ManifestQueries.Stalled(context, clearingHouseId, states, updatedBeforeUtc, maxCount);
            return await ManifestQueries.ToEntriesAsync(query, cancellationToken).ConfigureAwait(false);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}

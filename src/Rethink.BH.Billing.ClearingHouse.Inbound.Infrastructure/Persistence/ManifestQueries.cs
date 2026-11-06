using Microsoft.EntityFrameworkCore;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>Read-only manifest queries. All run without change tracking.</summary>
public static class ManifestQueries
{
    /// <summary>Rows in <paramref name="state"/>, oldest discovery first.</summary>
    public static IQueryable<InboundFileRecord> InState(InboundManifestDbContext context, InboundFileState state, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stateName = state.ToString();
        return context.Files.AsNoTracking()
            .Where(record => record.State == stateName)
            .OrderBy(record => record.DiscoveredUtc)
            .Take(maxCount);
    }

    /// <summary>Rows of one clearing house in <paramref name="states"/> not updated since <paramref name="updatedBeforeUtc"/>.</summary>
    public static IQueryable<InboundFileRecord> Stalled(
        InboundManifestDbContext context,
        int clearingHouseId,
        IReadOnlyCollection<InboundFileState> states,
        DateTime updatedBeforeUtc,
        int maxCount)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(states);

        var stateNames = states.Select(state => state.ToString()).ToArray();
        return context.Files.AsNoTracking()
            .Where(record => record.ClearingHouseId == clearingHouseId && stateNames.Contains(record.State) && record.UpdatedUtc < updatedBeforeUtc)
            .OrderBy(record => record.UpdatedUtc)
            .Take(maxCount);
    }

    /// <summary>Rows of one clearing house at any of <paramref name="remotePaths"/>.</summary>
    public static IQueryable<InboundFileRecord> AtPaths(InboundManifestDbContext context, int clearingHouseId, IReadOnlyCollection<string> remotePaths)
    {
        ArgumentNullException.ThrowIfNull(context);

        var paths = remotePaths.ToArray();
        return context.Files.AsNoTracking()
            .Where(record => record.ClearingHouseId == clearingHouseId && paths.Contains(record.RemotePath));
    }

    /// <summary>Runs <paramref name="query"/> and maps the rows to domain entries.</summary>
    public static async Task<IReadOnlyList<InboundFileEntry>> ToEntriesAsync(IQueryable<InboundFileRecord> query, CancellationToken cancellationToken)
    {
        var records = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return records.ConvertAll(InboundFileRecordMapper.ToEntry);
    }

    /// <summary>The entry with <paramref name="fileId"/>, or null.</summary>
    public static async Task<InboundFileEntry?> FindAsync(InboundManifestDbContext context, Guid fileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var record = await context.Files.AsNoTracking().FirstOrDefaultAsync(file => file.Id == fileId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : InboundFileRecordMapper.ToEntry(record);
    }

    /// <summary>Row count per state.</summary>
    public static async Task<Dictionary<InboundFileState, int>> CountByStateAsync(InboundManifestDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var counts = await context.Files
            .GroupBy(record => record.State)
            .Select(group => new StateCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byState = Enum.GetValues<InboundFileState>().ToDictionary(state => state, _ => 0);
        foreach (var count in counts)
        {
            byState[Enum.Parse<InboundFileState>(count.State)] = count.Count;
        }

        return byState;
    }

    /// <summary>Discovery time of the oldest unfinished row, or null when nothing is pending.</summary>
    public static async Task<DateTime?> OldestPendingAsync(InboundManifestDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var finished = PipelineBacklog.FinishedStates.Select(state => state.ToString()).ToArray();
        var oldest = await context.Files
            .Where(record => !finished.Contains(record.State))
            .MinAsync(record => (DateTime?)record.DiscoveredUtc, cancellationToken)
            .ConfigureAwait(false);
        return oldest is null ? null : DateTime.SpecifyKind(oldest.Value, DateTimeKind.Utc);
    }

    private sealed record StateCount(string State, int Count);
}

using Microsoft.EntityFrameworkCore;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>
/// Adds listed file versions that the manifest has not seen. A version is path + size + modified time, so a file
/// re-uploaded under the same name is a new version, while a file listed again is not.
/// </summary>
public static class ManifestRegistration
{
    /// <summary>
    /// Stages new rows on <paramref name="context"/> (the caller saves) and returns every row still <c>Discovered</c>:
    /// the new ones plus earlier ones whose queue send never completed.
    /// </summary>
    public static async Task<(List<InboundFileRecord> Pending, int Added)> StageAsync(
        InboundManifestDbContext context,
        int clearingHouseId,
        IReadOnlyList<RemoteFile> files,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        var paths = files.Select(file => file.RemotePath).Distinct(StringComparer.Ordinal).ToArray();
        var existing = await ManifestQueries.AtPaths(context, clearingHouseId, paths).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Merge(context, clearingHouseId, files, Index(existing), nowUtc);
    }

    private static (List<InboundFileRecord> Pending, int Added) Merge(
        InboundManifestDbContext context,
        int clearingHouseId,
        IReadOnlyList<RemoteFile> files,
        Dictionary<(string RemotePath, long SizeBytes, long ModifiedTicks), InboundFileRecord> known,
        DateTime nowUtc)
    {
        var pending = new List<InboundFileRecord>();
        var seen = new HashSet<(string RemotePath, long SizeBytes, long ModifiedTicks)>();
        var added = 0;
        foreach (var file in files)
        {
            var key = InboundFileRecordMapper.VersionKey(file.RemotePath, file.Size, file.ModifiedAtUtc);
            if (!seen.Add(key))
            {
                continue;
            }

            if (known.TryGetValue(key, out var record))
            {
                AddWhenDiscovered(pending, record);
                continue;
            }

            var created = InboundFileRecordMapper.NewRecord(clearingHouseId, file, nowUtc);
            context.Files.Add(created);
            pending.Add(created);
            added++;
        }

        return (pending, added);
    }

    private static Dictionary<(string RemotePath, long SizeBytes, long ModifiedTicks), InboundFileRecord> Index(List<InboundFileRecord> records)
    {
        var known = new Dictionary<(string RemotePath, long SizeBytes, long ModifiedTicks), InboundFileRecord>(records.Count);
        foreach (var record in records)
        {
            known.TryAdd(InboundFileRecordMapper.VersionKey(record.RemotePath, record.SizeBytes, record.RemoteModifiedUtc), record);
        }

        return known;
    }

    private static void AddWhenDiscovered(List<InboundFileRecord> pending, InboundFileRecord record)
    {
        if (string.Equals(record.State, nameof(InboundFileState.Discovered), StringComparison.Ordinal))
        {
            pending.Add(record);
        }
    }
}

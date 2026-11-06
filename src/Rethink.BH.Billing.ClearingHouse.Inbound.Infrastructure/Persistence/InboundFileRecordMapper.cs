using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>Maps manifest rows to domain entries. Dates read back from the database are marked UTC.</summary>
public static class InboundFileRecordMapper
{
    /// <summary>The domain view of <paramref name="record"/>.</summary>
    public static InboundFileEntry ToEntry(InboundFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new InboundFileEntry
        {
            Id = record.Id,
            ClearingHouseId = record.ClearingHouseId,
            RemotePath = record.RemotePath,
            FileName = record.FileName,
            SizeBytes = record.SizeBytes,
            RemoteModifiedUtc = AsUtc(record.RemoteModifiedUtc),
            State = Enum.Parse<InboundFileState>(record.State),
            Attempts = record.Attempts,
            LandingBlobName = record.LandingBlobName,
            Sha256 = record.Sha256,
            BillingFileId = record.BillingFileId,
            BillingQueuedUtc = AsUtc(record.BillingQueuedUtc),
            LastError = record.LastError,
            ClaimExpiresUtc = AsUtc(record.ClaimExpiresUtc),
            DiscoveredUtc = AsUtc(record.DiscoveredUtc),
            UpdatedUtc = AsUtc(record.UpdatedUtc),
        };
    }

    /// <summary>A new Discovered row for <paramref name="file"/>.</summary>
    public static InboundFileRecord NewRecord(int clearingHouseId, RemoteFile file, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new InboundFileRecord
        {
            Id = Guid.NewGuid(),
            ClearingHouseId = clearingHouseId,
            RemotePath = file.RemotePath,
            FileName = file.Name,
            SizeBytes = file.Size,
            RemoteModifiedUtc = AsUtc(file.ModifiedAtUtc),
            State = nameof(InboundFileState.Discovered),
            DiscoveredUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    /// <summary>Identity of one remote file version.</summary>
    public static (string RemotePath, long SizeBytes, long ModifiedTicks) VersionKey(string remotePath, long sizeBytes, DateTime modifiedUtc)
        => (remotePath, sizeBytes, AsUtc(modifiedUtc).Ticks);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value is null ? null : AsUtc(value.Value);
}

using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Backup store written before Billing receives a downloaded file.</summary>
public interface IBackupFileStore
{
    /// <summary>Writes <paramref name="fileName"/> once. An existing backup is left unchanged.</summary>
    Task<BackupWriteResult> WriteOnceAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

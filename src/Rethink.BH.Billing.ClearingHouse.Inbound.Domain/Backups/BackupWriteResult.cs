namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;

/// <summary>Result of writing a downloaded file to the backup container.</summary>
/// <param name="BlobPath">Blob name inside the backup container.</param>
/// <param name="AlreadyExisted">True when the blob was already present and was left unchanged.</param>
public sealed record BackupWriteResult(string BlobPath, bool AlreadyExisted);

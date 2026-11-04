namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Steps one file reached. Logged when the file fails, so support can tell where it stopped.</summary>
public sealed class InboundFileProgress
{
    /// <summary>The backup blob was written or already existed.</summary>
    public bool BackupStored { get; private set; }

    /// <summary>Billing accepted and queued the file.</summary>
    public bool BillingQueued { get; private set; }

    /// <summary>Records that the backup was written.</summary>
    public void MarkBackupStored() => BackupStored = true;

    /// <summary>Records that Billing queued the file.</summary>
    public void MarkBillingQueued() => BillingQueued = true;
}

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>Where a tracked file is in the pipeline. Stored by name, so values can be added without renumbering.</summary>
public enum InboundFileState
{
    /// <summary>Registered by the lister; no download message sent yet.</summary>
    Discovered,

    /// <summary>A download message was sent.</summary>
    Queued,

    /// <summary>A download worker is streaming the file into landing storage.</summary>
    Downloading,

    /// <summary>The file is in landing storage with a verified size and SHA-256 hash.</summary>
    Landed,

    /// <summary>A processor is backing up the file and registering it with Billing.</summary>
    Processing,

    /// <summary>Billing accepted and queued the file; only the remote delete is left.</summary>
    BillingQueued,

    /// <summary>A processor is deleting the remote file.</summary>
    Deleting,

    /// <summary>Finished: Billing queued the file and the remote copy was deleted (or delete is off).</summary>
    Done,

    /// <summary>The remote file changed after it was listed; the lister registers the new version separately.</summary>
    Superseded,

    /// <summary>Retries are exhausted. Needs an operator replay.</summary>
    Quarantined,
}

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

/// <summary>Failure text recorded on a file outcome. EDI content and secrets are never included.</summary>
public static class InboundFailureMessages
{
    /// <summary>Billing rejected the file or the call failed.</summary>
    public const string BillingRejected = "Billing rejected the file.";

    /// <summary>Billing accepted the file and did not publish it.</summary>
    public const string BillingDidNotQueue = "Billing did not queue the file.";

    /// <summary>The remote file could not be removed after Billing queued it.</summary>
    public const string RemoteDeleteFailed = "Remote delete failed.";

    /// <summary>A file failed and no shorter reason was available.</summary>
    public const string DownloadFailed = "Download failed.";
}

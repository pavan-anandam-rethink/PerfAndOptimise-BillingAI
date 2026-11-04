namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>Operation names written to logs. Searchable as the <c>Operation</c> custom dimension.</summary>
public static class OperationNames
{
    /// <summary>A whole download run. Matches the legacy endpoint name so existing queries keep working.</summary>
    public const string DownloadEdiDataFromSftp = "downloadEdiDataFromSftp";

    /// <summary>The parallel loop over listed files.</summary>
    public const string ProcessFiles = "ProcessFiles";

    /// <summary>Download, backup, and Billing registration of one file.</summary>
    public const string ProcessInboundFile = "ProcessInboundFile";

    /// <summary>Remote delete after Billing queued the file.</summary>
    public const string DeleteRemoteFile = "DeleteRemoteFile";
}

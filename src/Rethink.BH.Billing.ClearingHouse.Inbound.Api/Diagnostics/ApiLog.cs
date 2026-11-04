namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;

/// <summary>
/// API log events (3000–3099). Message text keeps the legacy wording so existing App Insights and Datadog queries still match.
/// </summary>
internal static partial class ApiLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "{Controller}: Starting EdiDownload. ClearingHouseId={ClearingHouseId}")]
    public static partial void DownloadStarting(ILogger logger, string controller, int clearingHouseId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "{Controller}: Invalid clearing house Id : {ClearingHouseId}")]
    public static partial void InvalidClearingHouse(ILogger logger, string controller, int clearingHouseId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{Controller}: No files found to download. ClearingHouseId={ClearingHouseId}")]
    public static partial void NoFilesDownloaded(ILogger logger, string controller, int clearingHouseId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "{Controller}: Download finished with files left on SFTP. ClearingHouseId={ClearingHouseId}, FilesSeen={FilesSeen}, LeftOnSftp={LeftOnSftp}")]
    public static partial void DownloadLeftFiles(ILogger logger, string controller, int clearingHouseId, int filesSeen, int leftOnSftp);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Controller}: Download finished for ClearingHouseId={ClearingHouseId}. FilesSeen={FilesSeen}, StoredInBackupAndFolders={Stored}")]
    public static partial void DownloadCompleted(ILogger logger, string controller, int clearingHouseId, int filesSeen, int stored);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "Starting {Controller}.{Action} {Method} {Path} {SafeIds}")]
    public static partial void ActionStarting(ILogger logger, string? controller, string? action, string method, string? path, string safeIds);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "Finished {Controller}.{Action} with status {StatusCode} in {ElapsedMilliseconds} ms.")]
    public static partial void ActionFinished(ILogger logger, string? controller, string? action, int statusCode, long elapsedMilliseconds);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Error, Message = "Request exception on {Path}. Method={Method} Controller={Controller} Action={Action} TraceIdentifier={TraceIdentifier} CorrelationId={CorrelationId}")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string? path, string method, object? controller, object? action, string traceIdentifier, string correlationId);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Warning, Message = "Request exception on {Path}. Method={Method} Controller={Controller} Action={Action} TraceIdentifier={TraceIdentifier} CorrelationId={CorrelationId}")]
    public static partial void RequestRejected(ILogger logger, Exception exception, string? path, string method, object? controller, object? action, string traceIdentifier, string correlationId);
}

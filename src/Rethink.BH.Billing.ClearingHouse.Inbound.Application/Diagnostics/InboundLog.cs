using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>
/// Source-generated log events for the inbound download. Each event has a stable id, so Application Insights
/// queries can filter on <c>customDimensions.EventId</c> instead of message text.
/// </summary>
public static partial class InboundLog
{
    /// <summary>Writes the run summary: <see cref="NoFilesFound"/> when nothing was processed, otherwise <see cref="RunFinished"/>.</summary>
    public static void RunCompleted(ILogger logger, ClearingHouseDescriptor clearingHouse, InboundDownloadResult result, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Files.Count == 0)
        {
            NoFilesFound(logger, clearingHouse.Title, result.Kind.ToString(), elapsedMilliseconds);
            return;
        }

        var leftOnSftp = result.FailedCount;
        RunFinished(logger, clearingHouse.Title, result.Kind.ToString(), result.Files.Count, result.Files.Count - leftOnSftp, leftOnSftp, elapsedMilliseconds);
    }

    /// <summary>A run started.</summary>
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Starting EdiDownload for {ClearingHouse}. ClearingHouseId={ClearingHouseId}")]
    public static partial void RunStarted(ILogger logger, string clearingHouse, int clearingHouseId);

    /// <summary>The requested clearing house id is not configured.</summary>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Invalid clearing house Id : {ClearingHouseId}")]
    public static partial void InvalidClearingHouse(ILogger logger, int clearingHouseId);

    /// <summary>The download directory was listed.</summary>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Listed {ListedCount} SFTP files, selected {SelectedCount}, with {BatchConcurrency} parallel workers.")]
    public static partial void FilesListed(ILogger logger, int listedCount, int selectedCount, int batchConcurrency);

    /// <summary>A run finished with files.</summary>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Download finished for {ClearingHouse}. RunKind={RunKind}, FilesSeen={FilesSeen}, StoredInBackupAndFolders={Stored}, LeftOnSftp={LeftOnSftp}, ElapsedMilliseconds={ElapsedMilliseconds}")]
    public static partial void RunFinished(ILogger logger, string clearingHouse, string runKind, int filesSeen, int stored, int leftOnSftp, long elapsedMilliseconds);

    /// <summary>A run finished without files.</summary>
    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "No files found to download for {ClearingHouse}. RunKind={RunKind}, ElapsedMilliseconds={ElapsedMilliseconds}")]
    public static partial void NoFilesFound(ILogger logger, string clearingHouse, string runKind, long elapsedMilliseconds);

    /// <summary>The backup blob was written or already existed.</summary>
    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Backup {BackupState} for {FileName}.")]
    public static partial void BackupWritten(ILogger logger, string backupState, string fileName);

    /// <summary>Billing answered a registration.</summary>
    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Billing registration for {FileName}. Accepted={Accepted}, Queued={Queued}, BillingFileId={BillingFileId}")]
    public static partial void BillingRegistered(ILogger logger, string fileName, bool accepted, bool queued, int? billingFileId);

    /// <summary>A file stays on SFTP for the next run.</summary>
    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "{FileName} stays on SFTP until it is in both the backup and the billing folder. BackupUploaded={BackupUploaded}, BillingUploaded={BillingUploaded}")]
    public static partial void FileLeftOnSftp(ILogger logger, string fileName, bool backupUploaded, bool billingUploaded);

    /// <summary>A file was stored and left on SFTP because remote delete is off.</summary>
    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "{FileName} stored and left on SFTP because remote delete is off.")]
    public static partial void RemoteDeleteDisabled(ILogger logger, string fileName);

    /// <summary>A file finished.</summary>
    [LoggerMessage(EventId = 1014, Level = LogLevel.Information, Message = "{FileName} finished with status {FileStatus}.")]
    public static partial void FileCompleted(ILogger logger, string fileName, string fileStatus);

    /// <summary>The fault boundary caught an exception.</summary>
    [LoggerMessage(EventId = 1020, Level = LogLevel.Error, Message = "Operation {OperationName} failed with {ExceptionType}.")]
    public static partial void OperationFaulted(ILogger logger, string operationName, string exceptionType, Exception exception);

    /// <summary>The run limit stopped an operation.</summary>
    [LoggerMessage(EventId = 1021, Level = LogLevel.Error, Message = "Operation {OperationName} stopped at the run limit. Remaining files stay on SFTP.")]
    public static partial void OperationTimedOut(ILogger logger, string operationName);

    /// <summary>A MediatR request started.</summary>
    [LoggerMessage(EventId = 1030, Level = LogLevel.Information, Message = "Handling {RequestName}.")]
    public static partial void RequestStarted(ILogger logger, string requestName);

    /// <summary>A MediatR request finished.</summary>
    [LoggerMessage(EventId = 1031, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds} ms.")]
    public static partial void RequestFinished(ILogger logger, string requestName, long elapsedMilliseconds);
}

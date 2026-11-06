using Microsoft.Extensions.Logging;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>
/// Source-generated log events for the queue pipeline (1100–1199). File ids, attempt numbers, and stages are in the log
/// scope (<see cref="LogScopes.ForWorkItem"/>), so every event of one file can be found by <c>FileId</c>.
/// </summary>
public static partial class PipelineLog
{
    /// <summary>A lister run started.</summary>
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Listing {ClearingHouse} for the pipeline. Mode={PipelineMode}")]
    public static partial void ListingStarted(ILogger logger, string clearingHouse, string pipelineMode);

    /// <summary>Another lister holds the lease.</summary>
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Skipped listing {ClearingHouse}: another lister holds the lease.")]
    public static partial void ListingLeaseHeld(ILogger logger, string clearingHouse);

    /// <summary>A lister run finished.</summary>
    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Listed {ClearingHouse}. Listed={ListedCount}, Stable={SelectedCount}, Enqueued={EnqueuedCount}, Requeued={RequeuedCount}")]
    public static partial void ListingCompleted(ILogger logger, string clearingHouse, int listedCount, int selectedCount, int enqueuedCount, int requeuedCount);

    /// <summary>Stalled files were sent again.</summary>
    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = "Re-enqueued {StalledCount} files untouched for {StalledAfterMinutes} minutes.")]
    public static partial void StalledFilesRequeued(ILogger logger, int stalledCount, long stalledAfterMinutes);

    /// <summary>A process message arrived while the pipeline is not active.</summary>
    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning, Message = "Process message completed without work: pipeline mode is {PipelineMode}.")]
    public static partial void ProcessingNotActive(ILogger logger, string pipelineMode);

    /// <summary>The pipeline is off.</summary>
    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Pipeline mode is Off; ClearingHouseId={ClearingHouseId} was not listed.")]
    public static partial void ListingDisabled(ILogger logger, int clearingHouseId);

    /// <summary>The lister was asked for an unknown clearing house.</summary>
    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "Cannot list ClearingHouseId={ClearingHouseId}: it is not configured.")]
    public static partial void ListingInvalidClearingHouse(ILogger logger, int clearingHouseId);

    /// <summary>A message refers to a file the manifest does not know.</summary>
    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning, Message = "{PipelineStage} message refers to an unknown file; completing it.")]
    public static partial void WorkItemOrphaned(ILogger logger, string pipelineStage);

    /// <summary>A duplicate or late message for a file that has moved on.</summary>
    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "{PipelineStage} message skipped: {FileName} is already {FileState}.")]
    public static partial void WorkItemAlreadyHandled(ILogger logger, string pipelineStage, string fileName, string fileState);

    /// <summary>Another worker claimed the file first.</summary>
    [LoggerMessage(EventId = 1112, Level = LogLevel.Information, Message = "{PipelineStage} message skipped: another worker claimed {FileName}.")]
    public static partial void WorkItemClaimLost(ILogger logger, string pipelineStage, string fileName);

    /// <summary>A file was streamed into landing storage.</summary>
    [LoggerMessage(EventId = 1113, Level = LogLevel.Information, Message = "Landed {FileName}. Bytes={Bytes}, Sha256={Sha256}, Mode={PipelineMode}")]
    public static partial void FileLanded(ILogger logger, string fileName, long bytes, string sha256, string pipelineMode);

    /// <summary>The remote file changed after listing.</summary>
    [LoggerMessage(EventId = 1114, Level = LogLevel.Warning, Message = "{FileName} changed after it was listed (listed {ListedBytes} bytes, downloaded {LandedBytes}). The next listing registers the new version.")]
    public static partial void FileSuperseded(ILogger logger, string fileName, long listedBytes, long landedBytes);

    /// <summary>Billing queued a landed file.</summary>
    [LoggerMessage(EventId = 1115, Level = LogLevel.Information, Message = "Billing queued {FileName}. BillingFileId={BillingFileId}, BackupState={BackupState}")]
    public static partial void FileRegistered(ILogger logger, string fileName, int? billingFileId, string backupState);

    /// <summary>A file finished.</summary>
    [LoggerMessage(EventId = 1116, Level = LogLevel.Information, Message = "{FileName} is done. RemoteDeleted={RemoteDeleted}")]
    public static partial void FileDone(ILogger logger, string fileName, bool remoteDeleted);

    /// <summary>A failed attempt will be retried.</summary>
    [LoggerMessage(EventId = 1120, Level = LogLevel.Warning, Message = "{PipelineStage} of {FileName} failed (attempt {Attempt}); retrying in {DelaySeconds} s. Reason={FailureReason}")]
    public static partial void RetryScheduled(ILogger logger, string pipelineStage, string fileName, int attempt, long delaySeconds, string failureReason);

    /// <summary>Retries are exhausted.</summary>
    [LoggerMessage(EventId = 1121, Level = LogLevel.Error, Message = "{PipelineStage} of {FileName} failed {Attempt} times; file quarantined. Reason={FailureReason}")]
    public static partial void FileQuarantined(ILogger logger, string pipelineStage, string fileName, int attempt, string failureReason);

    /// <summary>A message refers to a clearing house that is no longer configured.</summary>
    [LoggerMessage(EventId = 1122, Level = LogLevel.Error, Message = "{PipelineStage} message refers to ClearingHouseId={ClearingHouseId}, which is not configured.")]
    public static partial void WorkItemClearingHouseMissing(ILogger logger, string pipelineStage, int clearingHouseId);

    /// <summary>An operator replayed a quarantined file.</summary>
    [LoggerMessage(EventId = 1130, Level = LogLevel.Warning, Message = "Replaying quarantined {FileName} from {FileState} through {PipelineStage}.")]
    public static partial void FileReplayed(ILogger logger, string fileName, string fileState, string pipelineStage);
}

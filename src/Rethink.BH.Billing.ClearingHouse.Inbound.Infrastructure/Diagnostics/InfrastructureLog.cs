using Microsoft.Extensions.Logging;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

/// <summary>Source-generated log events for SFTP, blob, and Billing adapters. Credentials are never logged.</summary>
public static partial class InfrastructureLog
{
    /// <summary>The SFTP pool is opening its first connection.</summary>
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Opening SFTP pool. Host={SftpHost}, Port={SftpPort}, PoolSize={PoolSize}")]
    public static partial void SftpPoolOpening(ILogger logger, string sftpHost, int sftpPort, int poolSize);

    /// <summary>The first SFTP connection is ready.</summary>
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "SFTP pool connected. DownloadDirectory={DownloadDirectory}, ElapsedMilliseconds={ElapsedMilliseconds}")]
    public static partial void SftpPoolConnected(ILogger logger, string downloadDirectory, long elapsedMilliseconds);

    /// <summary>The download directory was listed.</summary>
    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Listed {FileCount} files in {DownloadDirectory}.")]
    public static partial void SftpDirectoryListed(ILogger logger, int fileCount, string downloadDirectory);

    /// <summary>A file was downloaded.</summary>
    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Downloaded {FileName}. Bytes={Bytes}")]
    public static partial void SftpFileDownloaded(ILogger logger, string fileName, long bytes);

    /// <summary>A transient SFTP failure will be retried.</summary>
    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "Retrying SFTP {OperationName} after {ExceptionType}. Attempt={Attempt}, DelayMilliseconds={DelayMilliseconds}")]
    public static partial void SftpRetryScheduled(ILogger logger, string operationName, string exceptionType, int attempt, long delayMilliseconds);

    /// <summary>No host key fingerprint is configured, so every host key is accepted.</summary>
    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "SFTP host key is not pinned for {SftpHost}. Configure HostKeyFingerprint to verify the server identity.")]
    public static partial void SftpHostKeyNotPinned(ILogger logger, string sftpHost);

    /// <summary>The server presented a host key that does not match the pinned fingerprint.</summary>
    [LoggerMessage(EventId = 2006, Level = LogLevel.Error, Message = "SFTP host key rejected for {SftpHost}. PresentedFingerprint={PresentedFingerprint}")]
    public static partial void SftpHostKeyRejected(ILogger logger, string sftpHost, string presentedFingerprint);

    /// <summary>The lister registered listed files in the manifest.</summary>
    [LoggerMessage(EventId = 2020, Level = LogLevel.Information, Message = "Manifest checked {ListedCount} files: {NewCount} new, {UnqueuedCount} registered earlier but never queued.")]
    public static partial void ManifestRegistered(ILogger logger, int listedCount, int newCount, int unqueuedCount);

    /// <summary>The lister lease could not be renewed; another lister may start after it expires.</summary>
    [LoggerMessage(EventId = 2021, Level = LogLevel.Error, Message = "Lost lease {LeaseName} while renewing it.")]
    public static partial void LeaseRenewalFailed(ILogger logger, string leaseName, Exception exception);

    /// <summary>The lister lease could not be released; it expires on its own.</summary>
    [LoggerMessage(EventId = 2022, Level = LogLevel.Warning, Message = "Could not release lease {LeaseName}; it expires within {LeaseSeconds} s.")]
    public static partial void LeaseReleaseFailed(ILogger logger, string leaseName, int leaseSeconds, Exception? exception);

    /// <summary>Service Bus reported an error outside a message handler (connection, lock renewal).</summary>
    [LoggerMessage(EventId = 2023, Level = LogLevel.Error, Message = "Service Bus error on {EntityPath} during {ErrorSource}.")]
    public static partial void ServiceBusError(ILogger logger, string entityPath, string errorSource, Exception exception);

    /// <summary>A message body could not be read; it is dead-lettered.</summary>
    [LoggerMessage(EventId = 2024, Level = LogLevel.Error, Message = "Dead-lettered message {MessageId} from {EntityPath}: the body is not a work item.")]
    public static partial void InvalidWorkMessage(ILogger logger, string messageId, string entityPath);

    /// <summary>A message was settled.</summary>
    [LoggerMessage(EventId = 2025, Level = LogLevel.Information, Message = "Settled message {MessageId} from {EntityPath} as {Disposition}. DeliveryCount={DeliveryCount}")]
    public static partial void WorkMessageSettled(ILogger logger, string messageId, string entityPath, string disposition, int deliveryCount);

    /// <summary>Work items were sent.</summary>
    [LoggerMessage(EventId = 2026, Level = LogLevel.Information, Message = "Sent {MessageCount} messages to {EntityPath}. DelaySeconds={DelaySeconds}")]
    public static partial void WorkMessagesSent(ILogger logger, int messageCount, string entityPath, long delaySeconds);

    /// <summary>A queue worker started or stopped.</summary>
    [LoggerMessage(EventId = 2027, Level = LogLevel.Information, Message = "{EntityPath} worker {WorkerState}. MaxConcurrentCalls={MaxConcurrentCalls}")]
    public static partial void WorkerStateChanged(ILogger logger, string entityPath, string workerState, int maxConcurrentCalls);

    /// <summary>Billing answered an inbound file registration.</summary>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Billing UploadEDIResponseFile answered {StatusCode} for {FileName}. RequestBytes={RequestBytes}, ElapsedMilliseconds={ElapsedMilliseconds}")]
    public static partial void BillingResponseReceived(ILogger logger, int statusCode, string fileName, long requestBytes, long elapsedMilliseconds);
}

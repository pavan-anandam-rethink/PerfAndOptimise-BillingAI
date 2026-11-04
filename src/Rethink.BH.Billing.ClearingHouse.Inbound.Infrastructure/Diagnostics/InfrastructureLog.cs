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

    /// <summary>Billing answered an inbound file registration.</summary>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Billing UploadEDIResponseFile answered {StatusCode} for {FileName}. RequestBytes={RequestBytes}, ElapsedMilliseconds={ElapsedMilliseconds}")]
    public static partial void BillingResponseReceived(ILogger logger, int statusCode, string fileName, long requestBytes, long elapsedMilliseconds);
}

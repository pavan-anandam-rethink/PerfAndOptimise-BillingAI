using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>
/// Download, then backup, then Billing. The remote file is deleted only after Billing accepted and queued it,
/// so a file is never lost: any earlier failure leaves it on SFTP for the next run.
/// </summary>
public sealed class InboundFileProcessor : IInboundFileProcessor
{
    private readonly IBackupFileStore _backupFileStore;
    private readonly IBillingFileRegistrar _billingFileRegistrar;
    private readonly IFaultBoundary _faultBoundary;
    private readonly IFailureMessageFormatter _failureMessageFormatter;
    private readonly bool _deleteRemoteFiles;
    private readonly ILogger<InboundFileProcessor> _logger;

    /// <summary>Creates the processor.</summary>
    public InboundFileProcessor(
        IBackupFileStore backupFileStore,
        IBillingFileRegistrar billingFileRegistrar,
        IFaultBoundary faultBoundary,
        IFailureMessageFormatter failureMessageFormatter,
        IOptions<InboundDownloadOptions> options,
        ILogger<InboundFileProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _backupFileStore = backupFileStore;
        _billingFileRegistrar = billingFileRegistrar;
        _faultBoundary = faultBoundary;
        _failureMessageFormatter = failureMessageFormatter;
        _deleteRemoteFiles = options.Value.DeleteRemoteFiles;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InboundFileOutcome> ProcessAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        RemoteFile file,
        InboundFileProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(progress);

        var registration = await StoreAndRegisterAsync(clearingHouse, session, file, progress, cancellationToken).ConfigureAwait(false);
        if (!registration.Accepted)
        {
            InboundLog.FileLeftOnSftp(_logger, file.Name, progress.BackupStored, progress.BillingQueued);
            return InboundFileOutcome.Failed(file.Name, InboundFileStatus.Stored, _failureMessageFormatter.Format(registration.Failure));
        }

        if (!registration.Queued)
        {
            InboundLog.FileLeftOnSftp(_logger, file.Name, progress.BackupStored, progress.BillingQueued);
            return InboundFileOutcome.Failed(file.Name, InboundFileStatus.Registered, InboundFailureMessages.BillingDidNotQueue);
        }

        if (!_deleteRemoteFiles)
        {
            InboundLog.RemoteDeleteDisabled(_logger, file.Name);
            return InboundFileOutcome.Completed(file.Name, InboundFileStatus.Queued);
        }

        return await DeleteRemoteFileAsync(session, file, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BillingRegistration> StoreAndRegisterAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        RemoteFile file,
        InboundFileProgress progress,
        CancellationToken cancellationToken)
    {
        // The pooled buffer is released here, before the remote delete round trip.
        using var content = await session.DownloadAsync(file, cancellationToken).ConfigureAwait(false);

        var backup = await _backupFileStore.WriteOnceAsync(file.Name, content.Memory, cancellationToken).ConfigureAwait(false);
        progress.MarkBackupStored();
        InboundLog.BackupWritten(_logger, backup.AlreadyExisted ? "already existed" : "uploaded", file.Name);

        var registration = await _billingFileRegistrar.RegisterAsync(clearingHouse, file.Name, content.Memory, cancellationToken).ConfigureAwait(false);
        if (registration.Accepted && registration.Queued)
        {
            progress.MarkBillingQueued();
        }

        InboundLog.BillingRegistered(_logger, file.Name, registration.Accepted, registration.Queued, registration.FileId);
        return registration;
    }

    private async Task<InboundFileOutcome> DeleteRemoteFileAsync(ISftpSession session, RemoteFile file, CancellationToken cancellationToken)
    {
        var deleted = await _faultBoundary.RunAsync(
            OperationNames.DeleteRemoteFile,
            async () =>
            {
                await session.DeleteAsync(file, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

        if (!deleted.IsSucceeded)
        {
            return InboundFileOutcome.Failed(file.Name, InboundFileStatus.Queued, InboundFailureMessages.RemoteDeleteFailed);
        }

        InboundLog.FileCompleted(_logger, file.Name, nameof(InboundFileStatus.RemoteDeleted));
        return InboundFileOutcome.Completed(file.Name, InboundFileStatus.RemoteDeleted);
    }
}

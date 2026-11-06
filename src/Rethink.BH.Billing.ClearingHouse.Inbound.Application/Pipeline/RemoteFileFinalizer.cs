using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Last step of a file Billing has queued: delete the remote copy (unless <c>DeleteRemoteFiles</c> is off) and mark the file
/// done. A failed delete returns the file to BillingQueued, so its retry repeats only the delete, never the Billing call.
/// </summary>
public sealed class RemoteFileFinalizer
{
    private readonly ISftpSessionProvider _sessionProvider;
    private readonly IInboundFileManifest _manifest;
    private readonly IWorkItemFailureHandler _failureHandler;
    private readonly IFaultBoundary _faultBoundary;
    private readonly bool _deleteRemoteFiles;
    private readonly ILogger<RemoteFileFinalizer> _logger;

    /// <summary>Creates the finalizer.</summary>
    public RemoteFileFinalizer(
        ISftpSessionProvider sessionProvider,
        IInboundFileManifest manifest,
        IWorkItemFailureHandler failureHandler,
        IFaultBoundary faultBoundary,
        IOptions<InboundDownloadOptions> options,
        ILogger<RemoteFileFinalizer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessionProvider = sessionProvider;
        _manifest = manifest;
        _failureHandler = failureHandler;
        _faultBoundary = faultBoundary;
        _deleteRemoteFiles = options.Value.DeleteRemoteFiles;
        _logger = logger;
    }

    /// <summary>Finishes a file claimed in <see cref="InboundFileState.Deleting"/>.</summary>
    public async Task<WorkItemDisposition> FinishAsync(InboundFileWorkItem workItem, WorkItemClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.IsClaimed)
        {
            return claim.Disposition;
        }

        var entry = claim.Entry;
        if (!_deleteRemoteFiles)
        {
            InboundLog.RemoteDeleteDisabled(_logger, entry.FileName);
        }
        else if (!await TryDeleteAsync(claim.ClearingHouse, entry, cancellationToken).ConfigureAwait(false))
        {
            var failure = new WorkItemFailure(InboundWorkStage.Process, workItem, entry, InboundFileState.Deleting, InboundFileState.BillingQueued, InboundFailureMessages.RemoteDeleteFailed);
            return await _failureHandler.HandleAsync(failure, cancellationToken).ConfigureAwait(false);
        }

        await _manifest.TryTransitionAsync(InboundFileTransition.Create(entry.Id, InboundFileState.Done, InboundFileState.Deleting), cancellationToken).ConfigureAwait(false);
        PipelineLog.FileDone(_logger, entry.FileName, _deleteRemoteFiles);
        return WorkItemDisposition.Completed;
    }

    private async Task<bool> TryDeleteAsync(ClearingHouseDescriptor clearingHouse, InboundFileEntry entry, CancellationToken cancellationToken)
    {
        var deleted = await _faultBoundary.RunAsync(
            OperationNames.DeleteRemoteFile,
            async () =>
            {
                var session = await _sessionProvider.GetAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
                await session.DeleteAsync(entry.ToRemoteFile(), cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

        return deleted.IsSucceeded;
    }
}

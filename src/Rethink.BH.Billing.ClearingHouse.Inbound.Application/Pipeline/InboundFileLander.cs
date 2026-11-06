using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Streams a remote file straight into landing storage (no full in-memory copy), then records the result. A size that
/// differs from the listing means the file changed after it was listed: that version is superseded and the next listing
/// registers the new one. In Active mode a landed file is sent to the process queue.
/// </summary>
public sealed class InboundFileLander
{
    private readonly ISftpSessionProvider _sessionProvider;
    private readonly ILandingFileStore _landingStore;
    private readonly IInboundFileManifest _manifest;
    private readonly IInboundWorkSender _workSender;
    private readonly PipelineOptions _options;
    private readonly ILogger<InboundFileLander> _logger;

    /// <summary>Creates the lander.</summary>
    public InboundFileLander(
        ISftpSessionProvider sessionProvider,
        ILandingFileStore landingStore,
        IInboundFileManifest manifest,
        IInboundWorkSender workSender,
        IOptions<PipelineOptions> options,
        ILogger<InboundFileLander> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessionProvider = sessionProvider;
        _landingStore = landingStore;
        _manifest = manifest;
        _workSender = workSender;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Streams <paramref name="entry"/> into its landing blob.</summary>
    public async Task<LandedFile> LandAsync(ClearingHouseDescriptor clearingHouse, InboundFileEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var session = await _sessionProvider.GetAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
        var remoteFile = entry.ToRemoteFile();
        return await _landingStore
            .WriteAsync(LandingBlobNames.For(entry), (destination, token) => session.CopyToAsync(remoteFile, destination, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Moves the file to Landed (or Superseded) and, in Active mode, sends it to processing.</summary>
    public async Task<WorkItemDisposition> RecordAsync(InboundFileEntry entry, LandedFile landed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(landed);

        if (landed.Length != entry.SizeBytes)
        {
            PipelineLog.FileSuperseded(_logger, entry.FileName, entry.SizeBytes, landed.Length);
            var superseded = InboundFileTransition.Create(entry.Id, InboundFileState.Superseded, InboundFileState.Downloading) with { LandingBlobName = landed.BlobName };
            await _manifest.TryTransitionAsync(superseded, cancellationToken).ConfigureAwait(false);
            return WorkItemDisposition.Completed;
        }

        var transition = InboundFileTransition.Create(entry.Id, InboundFileState.Landed, InboundFileState.Downloading) with
        {
            LandingBlobName = landed.BlobName,
            Sha256 = landed.Sha256,
        };
        await _manifest.TryTransitionAsync(transition, cancellationToken).ConfigureAwait(false);
        PipelineLog.FileLanded(_logger, entry.FileName, landed.Length, landed.Sha256, _options.Mode.ToString());

        if (_options.IsActive)
        {
            await _workSender.SendAsync(InboundWorkStage.Process, [InboundFileWorkItem.First(entry)], cancellationToken).ConfigureAwait(false);
        }

        return WorkItemDisposition.Completed;
    }
}

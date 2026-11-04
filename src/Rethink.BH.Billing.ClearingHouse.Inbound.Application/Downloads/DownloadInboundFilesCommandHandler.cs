using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>
/// One download run: open SFTP, list, select, process the files in parallel, and summarize.
/// The run limit (<c>MaxRunMinutes</c>) ends the run early with whatever finished; remaining files stay on SFTP.
/// </summary>
public sealed class DownloadInboundFilesCommandHandler : IRequestHandler<DownloadInboundFilesCommand, InboundDownloadResult>
{
    private readonly IClearingHouseCatalog _clearingHouseCatalog;
    private readonly ISftpSessionFactory _sftpSessionFactory;
    private readonly IRemoteFileSelector _remoteFileSelector;
    private readonly IInboundFileBatchProcessor _batchProcessor;
    private readonly IFaultBoundary _faultBoundary;
    private readonly TimeProvider _timeProvider;
    private readonly InboundDownloadOptions _options;
    private readonly ILogger<DownloadInboundFilesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public DownloadInboundFilesCommandHandler(
        IClearingHouseCatalog clearingHouseCatalog,
        ISftpSessionFactory sftpSessionFactory,
        IRemoteFileSelector remoteFileSelector,
        IInboundFileBatchProcessor batchProcessor,
        IFaultBoundary faultBoundary,
        TimeProvider timeProvider,
        IOptions<InboundDownloadOptions> options,
        ILogger<DownloadInboundFilesCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clearingHouseCatalog = clearingHouseCatalog;
        _sftpSessionFactory = sftpSessionFactory;
        _remoteFileSelector = remoteFileSelector;
        _batchProcessor = batchProcessor;
        _faultBoundary = faultBoundary;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InboundDownloadResult> Handle(DownloadInboundFilesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clearingHouse = _clearingHouseCatalog.FindById(request.ClearingHouseId);
        if (clearingHouse is null)
        {
            InboundLog.InvalidClearingHouse(_logger, request.ClearingHouseId);
            return InboundDownloadResult.Invalid(request.ClearingHouseId);
        }

        using var runScope = _logger.BeginScope(LogScopes.ForRun(clearingHouse, Guid.NewGuid().ToString("N")));
        var startedAt = _timeProvider.GetTimestamp();
        InboundLog.RunStarted(_logger, clearingHouse.Title, clearingHouse.Id);

        var result = await RunWithinLimitAsync(clearingHouse, cancellationToken).ConfigureAwait(false);

        InboundLog.RunCompleted(_logger, clearingHouse, result, (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        return result;
    }

    private static InboundDownloadResult ToResult(int clearingHouseId, GuardedResult<IReadOnlyList<InboundFileOutcome>> guarded)
        => guarded.Kind switch
        {
            GuardedResultKind.Succeeded => InboundDownloadResult.FromOutcomes(clearingHouseId, guarded.Value!),
            GuardedResultKind.TimedOut => InboundDownloadResult.Empty(clearingHouseId),
            _ => InboundDownloadResult.ConnectionFailed(clearingHouseId),
        };

    private async Task<InboundDownloadResult> RunWithinLimitAsync(ClearingHouseDescriptor clearingHouse, CancellationToken callerToken)
    {
        using var runLimit = new CancellationTokenSource(_options.MaxRunDuration, _timeProvider);
        using var runToken = CancellationTokenSource.CreateLinkedTokenSource(runLimit.Token, callerToken);

        var guarded = await _faultBoundary
            .RunWithRunLimitAsync(
                OperationNames.DownloadEdiDataFromSftp,
                () => DownloadAsync(clearingHouse, runToken.Token, callerToken),
                callerToken)
            .ConfigureAwait(false);

        return ToResult(clearingHouse.Id, guarded);
    }

    private async Task<IReadOnlyList<InboundFileOutcome>> DownloadAsync(
        ClearingHouseDescriptor clearingHouse,
        CancellationToken runToken,
        CancellationToken callerToken)
    {
        var session = await _sftpSessionFactory.OpenAsync(clearingHouse, runToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var listed = await session.ListAsync(runToken).ConfigureAwait(false);
            var selected = _remoteFileSelector.SelectForProcessing(listed);
            InboundLog.FilesListed(_logger, listed.Count, selected.Count, _options.BatchConcurrency);

            return await _batchProcessor.ProcessAsync(clearingHouse, session, selected, runToken, callerToken).ConfigureAwait(false);
        }
    }
}

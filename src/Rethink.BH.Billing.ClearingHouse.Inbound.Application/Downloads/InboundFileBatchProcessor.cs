using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>
/// Runs <see cref="IInboundFileProcessor"/> for each file with <c>BatchConcurrency</c> workers.
/// Each file runs inside its own log scope and fault boundary, so one failing file never stops the others.
/// </summary>
public sealed class InboundFileBatchProcessor : IInboundFileBatchProcessor
{
    private readonly IInboundFileProcessor _inboundFileProcessor;
    private readonly IFaultBoundary _faultBoundary;
    private readonly IFailureMessageFormatter _failureMessageFormatter;
    private readonly int _batchConcurrency;
    private readonly ILogger<InboundFileBatchProcessor> _logger;

    /// <summary>Creates the batch processor.</summary>
    public InboundFileBatchProcessor(
        IInboundFileProcessor inboundFileProcessor,
        IFaultBoundary faultBoundary,
        IFailureMessageFormatter failureMessageFormatter,
        IOptions<InboundDownloadOptions> options,
        ILogger<InboundFileBatchProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _inboundFileProcessor = inboundFileProcessor;
        _faultBoundary = faultBoundary;
        _failureMessageFormatter = failureMessageFormatter;
        _batchConcurrency = options.Value.BatchConcurrency;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundFileOutcome>> ProcessAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        IReadOnlyList<RemoteFile> files,
        CancellationToken runToken,
        CancellationToken callerToken)
    {
        var outcomes = new ConcurrentQueue<InboundFileOutcome>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _batchConcurrency, CancellationToken = runToken };

        await _faultBoundary
            .RunWithRunLimitAsync(
                OperationNames.ProcessFiles,
                async () =>
                {
                    await Parallel.ForEachAsync(
                            files,
                            parallelOptions,
                            async (file, token) => outcomes.Enqueue(await ProcessOneAsync(clearingHouse, session, file, token).ConfigureAwait(false)))
                        .ConfigureAwait(false);
                    return true;
                },
                callerToken)
            .ConfigureAwait(false);

        return outcomes.ToArray();
    }

    private async Task<InboundFileOutcome> ProcessOneAsync(
        ClearingHouseDescriptor clearingHouse,
        ISftpSession session,
        RemoteFile file,
        CancellationToken cancellationToken)
    {
        using var fileScope = _logger.BeginScope(LogScopes.ForFile(file));
        var progress = new InboundFileProgress();

        var guarded = await _faultBoundary
            .RunAsync(
                OperationNames.ProcessInboundFile,
                () => _inboundFileProcessor.ProcessAsync(clearingHouse, session, file, progress, cancellationToken))
            .ConfigureAwait(false);

        if (guarded.IsSucceeded)
        {
            return guarded.Value!;
        }

        InboundLog.FileLeftOnSftp(_logger, file.Name, progress.BackupStored, progress.BillingQueued);
        return InboundFileOutcome.Failed(file.Name, InboundFileStatus.Failed, _failureMessageFormatter.Format(guarded.Exception?.Message));
    }
}

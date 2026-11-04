using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Renci.SshNet.Common;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>
/// Retry pipelines for idempotent SFTP operations (connect, list, download). Delete is never retried because a repeated
/// delete of an already removed file would report a false failure. Only connection-level faults are retried;
/// permission and missing-path errors fail immediately.
/// </summary>
public sealed class SftpRetryPipelines
{
    /// <summary>Operation name for opening the pool.</summary>
    public const string OpenOperation = "Open";

    /// <summary>Operation name for listing the directory.</summary>
    public const string ListOperation = "List";

    /// <summary>Operation name for downloading a file.</summary>
    public const string DownloadOperation = "Download";

    /// <summary>Builds the pipelines from <see cref="InboundDownloadOptions.SftpRetryCount"/>.</summary>
    public SftpRetryPipelines(IOptions<InboundDownloadOptions> options, TimeProvider timeProvider, ILogger<SftpRetryPipelines> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        Open = Build(OpenOperation, options.Value, timeProvider, logger);
        List = Build(ListOperation, options.Value, timeProvider, logger);
        Download = Build(DownloadOperation, options.Value, timeProvider, logger);
    }

    /// <summary>Pipeline for opening the pool.</summary>
    public ResiliencePipeline Open { get; }

    /// <summary>Pipeline for listing the directory.</summary>
    public ResiliencePipeline List { get; }

    /// <summary>Pipeline for downloading a file.</summary>
    public ResiliencePipeline Download { get; }

    /// <summary>True for faults worth retrying: dropped connections, timeouts, and socket errors.</summary>
    public static bool IsTransient(Exception exception)
        => exception is SshConnectionException or SshOperationTimeoutException or SocketException;

    private static ResiliencePipeline Build(string operationName, InboundDownloadOptions options, TimeProvider timeProvider, ILogger logger)
    {
        if (options.SftpRetryCount == 0)
        {
            return ResiliencePipeline.Empty;
        }

        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.SftpRetryCount,
                Delay = TimeSpan.FromMilliseconds(options.SftpRetryBaseDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = arguments => ValueTask.FromResult(arguments.Outcome.Exception is { } exception && IsTransient(exception)),
                OnRetry = arguments =>
                {
                    InfrastructureLog.SftpRetryScheduled(
                        logger,
                        operationName,
                        arguments.Outcome.Exception?.GetType().Name ?? "None",
                        arguments.AttemptNumber + 1,
                        (long)arguments.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }
}

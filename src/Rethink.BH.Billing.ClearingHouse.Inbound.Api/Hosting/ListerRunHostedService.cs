using MediatR;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>
/// Lister role: lists each configured clearing house once, sets the exit code, and stops the host so the CronJob pod
/// completes. The exit code starts at 1, so a run that ends with an unhandled error (logged by the host) is reported as
/// failed; it becomes 0 only when no clearing house failed.
/// </summary>
public sealed class ListerRunHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PipelineOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ListerRunHostedService> _logger;

    /// <summary>Creates the service.</summary>
    public ListerRunHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<PipelineOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<ListerRunHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>Process exit code for the run: 1 when any clearing house failed or is not configured.</summary>
    public static int ExitCodeFor(IReadOnlyCollection<InboundListingResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Any(result => result.Kind is InboundListingKind.Failed or InboundListingKind.Invalid) ? 1 : 0;
    }

    /// <summary>Lists each clearing house in turn.</summary>
    public static async Task<IReadOnlyList<InboundListingResult>> ListAllAsync(ISender sender, IReadOnlyList<int> clearingHouseIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(clearingHouseIds);

        var results = new List<InboundListingResult>(clearingHouseIds.Count);
        foreach (var clearingHouseId in clearingHouseIds)
        {
            results.Add(await sender.Send(new ListInboundFilesCommand(clearingHouseId), cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Environment.ExitCode = 1;
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var results = await ListAllAsync(scope.ServiceProvider.GetRequiredService<ISender>(), _options.ClearingHouseIds, stoppingToken).ConfigureAwait(false);
            Environment.ExitCode = Report(results);
        }

        _lifetime.StopApplication();
    }

    private int Report(IReadOnlyList<InboundListingResult> results)
    {
        var exitCode = ExitCodeFor(results);
        ApiLog.ListerRunFinished(_logger, results.Count, results.Count(result => result.Kind == InboundListingKind.Failed), exitCode);
        return exitCode;
    }
}

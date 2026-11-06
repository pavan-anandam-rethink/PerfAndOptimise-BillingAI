using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// The scheduled lister entry point. It never downloads: it checks the mode and clearing house, holds a lease so runs for
/// one clearing house cannot overlap, and runs <see cref="InboundFileLister"/> inside the fault boundary.
/// </summary>
public sealed class ListInboundFilesCommandHandler : IRequestHandler<ListInboundFilesCommand, InboundListingResult>
{
    /// <summary>Lease name prefix; the clearing house id is appended.</summary>
    public const string LeaseNamePrefix = "inbound-lister-";

    private readonly IClearingHouseCatalog _clearingHouseCatalog;
    private readonly IRunLeaseProvider _leaseProvider;
    private readonly InboundFileLister _lister;
    private readonly IFaultBoundary _faultBoundary;
    private readonly PipelineOptions _options;
    private readonly ILogger<ListInboundFilesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ListInboundFilesCommandHandler(
        IClearingHouseCatalog clearingHouseCatalog,
        IRunLeaseProvider leaseProvider,
        InboundFileLister lister,
        IFaultBoundary faultBoundary,
        IOptions<PipelineOptions> options,
        ILogger<ListInboundFilesCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clearingHouseCatalog = clearingHouseCatalog;
        _leaseProvider = leaseProvider;
        _lister = lister;
        _faultBoundary = faultBoundary;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InboundListingResult> Handle(ListInboundFilesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsEnabled)
        {
            PipelineLog.ListingDisabled(_logger, request.ClearingHouseId);
            return InboundListingResult.NotRun(request.ClearingHouseId, InboundListingKind.Disabled);
        }

        var clearingHouse = _clearingHouseCatalog.FindById(request.ClearingHouseId);
        if (clearingHouse is null)
        {
            PipelineLog.ListingInvalidClearingHouse(_logger, request.ClearingHouseId);
            return InboundListingResult.NotRun(request.ClearingHouseId, InboundListingKind.Invalid);
        }

        using var runScope = _logger.BeginScope(LogScopes.ForRun(clearingHouse, Guid.NewGuid().ToString("N")));
        return await ListUnderLeaseAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InboundListingResult> ListUnderLeaseAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        var lease = await _leaseProvider.TryAcquireAsync(LeaseNamePrefix + clearingHouse.Id, cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            PipelineLog.ListingLeaseHeld(_logger, clearingHouse.Title);
            return InboundListingResult.NotRun(clearingHouse.Id, InboundListingKind.LeaseHeld);
        }

        await using (lease.ConfigureAwait(false))
        {
            PipelineLog.ListingStarted(_logger, clearingHouse.Title, _options.Mode.ToString());
            var guarded = await _faultBoundary
                .RunAsync(OperationNames.ListInboundFiles, () => _lister.ListAsync(clearingHouse, cancellationToken))
                .ConfigureAwait(false);

            return guarded.IsSucceeded ? guarded.Value! : InboundListingResult.NotRun(clearingHouse.Id, InboundListingKind.Failed);
        }
    }
}

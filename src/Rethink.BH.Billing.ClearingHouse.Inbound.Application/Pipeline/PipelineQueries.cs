using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Counts by state and the age of the oldest unfinished file.</summary>
public sealed record GetPipelineBacklogQuery : IRequest<PipelineBacklog>;

/// <summary>Quarantined files, oldest first.</summary>
/// <param name="MaxCount">Most files returned.</param>
public sealed record ListQuarantinedFilesQuery(int MaxCount) : IRequest<IReadOnlyList<InboundFileEntry>>;

/// <summary>Answers the operational queries from the manifest.</summary>
public sealed class PipelineQueryHandler :
    IRequestHandler<GetPipelineBacklogQuery, PipelineBacklog>,
    IRequestHandler<ListQuarantinedFilesQuery, IReadOnlyList<InboundFileEntry>>
{
    /// <summary>Largest page an operator can request.</summary>
    public const int MaxPageSize = 500;

    private readonly IInboundFileManifest _manifest;

    /// <summary>Creates the handler.</summary>
    public PipelineQueryHandler(IInboundFileManifest manifest)
    {
        _manifest = manifest;
    }

    /// <inheritdoc />
    public Task<PipelineBacklog> Handle(GetPipelineBacklogQuery request, CancellationToken cancellationToken)
        => _manifest.GetBacklogAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<InboundFileEntry>> Handle(ListQuarantinedFilesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _manifest.ListByStateAsync(InboundFileState.Quarantined, Math.Clamp(request.MaxCount, 1, MaxPageSize), cancellationToken);
    }
}

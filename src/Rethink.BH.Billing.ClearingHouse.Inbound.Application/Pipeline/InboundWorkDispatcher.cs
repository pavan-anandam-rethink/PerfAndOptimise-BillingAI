using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Runs the command for one queue message.</summary>
public interface IInboundWorkDispatcher
{
    /// <summary>Handles <paramref name="item"/> for <paramref name="stage"/> and says how to settle the message.</summary>
    Task<WorkItemDisposition> DispatchAsync(InboundWorkStage stage, InboundFileWorkItem item, CancellationToken cancellationToken);
}

/// <summary>Maps a stage to its command so queue adapters stay unaware of MediatR.</summary>
public sealed class InboundWorkDispatcher : IInboundWorkDispatcher
{
    private readonly ISender _sender;

    /// <summary>Creates the dispatcher.</summary>
    public InboundWorkDispatcher(ISender sender) => _sender = sender;

    /// <inheritdoc />
    public Task<WorkItemDisposition> DispatchAsync(InboundWorkStage stage, InboundFileWorkItem item, CancellationToken cancellationToken)
        => stage switch
        {
            InboundWorkStage.Download => _sender.Send(new DownloadInboundFileCommand(item), cancellationToken),
            InboundWorkStage.Process => _sender.Send(new ProcessLandedFileCommand(item), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown pipeline stage."),
        };
}

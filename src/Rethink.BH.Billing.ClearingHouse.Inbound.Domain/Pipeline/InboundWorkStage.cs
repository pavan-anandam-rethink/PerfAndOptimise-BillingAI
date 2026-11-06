namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>Pipeline stage of a queue message. Each stage has its own queue.</summary>
public enum InboundWorkStage
{
    /// <summary>Stream the remote file into landing storage.</summary>
    Download,

    /// <summary>Back up, register with Billing, and delete the remote file.</summary>
    Process,
}

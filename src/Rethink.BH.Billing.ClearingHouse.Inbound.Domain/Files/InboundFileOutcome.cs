namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

/// <summary>Per-file result used to classify a run. It never contains EDI content.</summary>
/// <param name="FileName">Remote file name.</param>
/// <param name="Status">Last step the file reached.</param>
/// <param name="Failure">Failure reason, or null when the file finished.</param>
public sealed record InboundFileOutcome(string FileName, InboundFileStatus Status, string? Failure)
{
    /// <summary>True when the file stays on SFTP and the run reports a failure.</summary>
    public bool IsFailed => !string.IsNullOrEmpty(Failure);

    /// <summary>Creates a finished outcome.</summary>
    public static InboundFileOutcome Completed(string fileName, InboundFileStatus status) => new(fileName, status, null);

    /// <summary>Creates a failed outcome.</summary>
    public static InboundFileOutcome Failed(string fileName, InboundFileStatus status, string failure) => new(fileName, status, failure);
}

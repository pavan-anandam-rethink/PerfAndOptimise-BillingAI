namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Shortens a failure reason before it is recorded on a file outcome.</summary>
public interface IFailureMessageFormatter
{
    /// <summary>Returns a trimmed reason no longer than the configured maximum, or a default reason when blank.</summary>
    string Format(string? reason);
}

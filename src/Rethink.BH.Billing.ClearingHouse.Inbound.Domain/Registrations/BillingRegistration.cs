using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

/// <summary>Result of asking Billing to store and publish one downloaded file.</summary>
/// <param name="Accepted">Billing stored the file.</param>
/// <param name="Queued">Billing published the file for response processing.</param>
/// <param name="FileId">Billing file id when the response includes one.</param>
/// <param name="Failure">Failure reason, or null on success.</param>
public sealed record BillingRegistration(bool Accepted, bool Queued, int? FileId, string? Failure)
{
    /// <summary>Billing rejected the file or the call failed.</summary>
    public static BillingRegistration Rejected(string? failure)
        => new(false, false, null, string.IsNullOrWhiteSpace(failure) ? InboundFailureMessages.BillingRejected : failure);

    /// <summary>Billing accepted the file but did not publish it.</summary>
    public static BillingRegistration NotQueued(int? fileId) => new(true, false, fileId, InboundFailureMessages.BillingDidNotQueue);

    /// <summary>Billing accepted and published the file.</summary>
    public static BillingRegistration Succeeded(int? fileId) => new(true, true, fileId, null);
}

using Microsoft.EntityFrameworkCore;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>
/// Translates an <see cref="InboundFileTransition"/> into one conditional <c>UPDATE</c>. Optional values use
/// <c>COALESCE(@value, column)</c>, so a null leaves the stored value unchanged.
/// </summary>
public static class ManifestTransitionCommand
{
    /// <summary>Runs the update and returns the number of rows changed (0 or 1).</summary>
    public static Task<int> ExecuteAsync(InboundManifestDbContext context, InboundFileTransition transition, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(transition);

        var toState = transition.ToState.ToString();
        var claimExpires = transition.ClaimExpiresUtc;
        var attempts = transition.Attempts;
        var landingBlobName = transition.LandingBlobName;
        var sha256 = transition.Sha256;
        var billingFileId = transition.BillingFileId;
        var billingQueuedUtc = transition.BillingQueuedUtc;
        var lastError = Truncate(transition.LastError);

        return Matching(context, transition)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.State, toState)
                    .SetProperty(record => record.UpdatedUtc, nowUtc)
                    .SetProperty(record => record.ClaimExpiresUtc, record => claimExpires ?? record.ClaimExpiresUtc)
                    .SetProperty(record => record.Attempts, record => attempts ?? record.Attempts)
                    .SetProperty(record => record.LandingBlobName, record => landingBlobName ?? record.LandingBlobName)
                    .SetProperty(record => record.Sha256, record => sha256 ?? record.Sha256)
                    .SetProperty(record => record.BillingFileId, record => billingFileId ?? record.BillingFileId)
                    .SetProperty(record => record.BillingQueuedUtc, record => billingQueuedUtc ?? record.BillingQueuedUtc)
                    .SetProperty(record => record.LastError, record => lastError ?? record.LastError),
                cancellationToken);
    }

    /// <summary>Moves the given rows from Discovered to Queued; rows in any other state are left alone.</summary>
    public static Task<int> MarkQueuedAsync(InboundManifestDbContext context, IReadOnlyCollection<Guid> fileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fileIds);

        var ids = fileIds.ToArray();
        var discovered = nameof(InboundFileState.Discovered);
        var queued = nameof(InboundFileState.Queued);
        return context.Files
            .Where(record => ids.Contains(record.Id) && record.State == discovered)
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.State, queued).SetProperty(record => record.UpdatedUtc, nowUtc), cancellationToken);
    }

    /// <summary>
    /// The row to change: in one of the expected states, or (when reclaiming) already in the target state with an
    /// expired claim, which means the previous worker died mid-step.
    /// </summary>
    private static IQueryable<InboundFileRecord> Matching(InboundManifestDbContext context, InboundFileTransition transition)
    {
        var fileId = transition.FileId;
        var fromStates = transition.FromStates.Select(state => state.ToString()).ToArray();
        var toState = transition.ToState.ToString();
        var reclaimBefore = transition.ReclaimExpiredBeforeUtc;
        var reclaim = reclaimBefore is not null;

        return context.Files.Where(record => record.Id == fileId
            && (fromStates.Contains(record.State)
                || (reclaim && record.State == toState && record.ClaimExpiresUtc != null && record.ClaimExpiresUtc < reclaimBefore)));
    }

    private static string? Truncate(string? value)
        => value is { Length: > InboundManifestDbContext.LastErrorMaxLength } ? value[..InboundManifestDbContext.LastErrorMaxLength] : value;
}

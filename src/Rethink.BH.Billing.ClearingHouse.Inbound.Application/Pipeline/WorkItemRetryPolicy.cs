using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Exponential backoff for failed work items: base, 2×base, 4×base, … capped at the maximum delay.</summary>
public sealed class WorkItemRetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    /// <summary>Creates the policy.</summary>
    public WorkItemRetryPolicy(IOptions<PipelineOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        MaxAttempts = options.Value.MaxAttempts;
        _baseDelay = TimeSpan.FromSeconds(options.Value.RetryBaseDelaySeconds);
        _maxDelay = TimeSpan.FromSeconds(options.Value.RetryMaxDelaySeconds);
    }

    /// <summary>Failed attempts after which a file is quarantined.</summary>
    public int MaxAttempts { get; }

    /// <summary>True when <paramref name="failedAttempts"/> has used up every attempt.</summary>
    public bool IsExhausted(int failedAttempts) => failedAttempts >= MaxAttempts;

    /// <summary>Delay before the retry that follows the <paramref name="failedAttempts"/>-th failure.</summary>
    public TimeSpan DelayAfter(int failedAttempts)
    {
        var exponent = Math.Clamp(failedAttempts - 1, 0, 30);
        var delay = _baseDelay.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(delay, _maxDelay.TotalSeconds));
    }
}

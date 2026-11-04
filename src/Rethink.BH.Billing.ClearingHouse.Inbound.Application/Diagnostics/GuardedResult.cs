namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>How a guarded operation ended.</summary>
public enum GuardedResultKind
{
    /// <summary>The operation returned a value.</summary>
    Succeeded,

    /// <summary>The operation threw. The exception was logged by the fault boundary.</summary>
    Faulted,

    /// <summary>The run limit elapsed before the operation finished.</summary>
    TimedOut,
}

/// <summary>Creates <see cref="GuardedResult{T}"/> values.</summary>
public static class GuardedResult
{
    /// <summary>Creates a successful result.</summary>
    public static GuardedResult<T> Success<T>(T value) => new(GuardedResultKind.Succeeded, value, null);

    /// <summary>Creates a faulted result.</summary>
    public static GuardedResult<T> Fault<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new GuardedResult<T>(GuardedResultKind.Faulted, default, exception);
    }

    /// <summary>Creates a timed-out result.</summary>
    public static GuardedResult<T> TimedOut<T>() => new(GuardedResultKind.TimedOut, default, null);
}

/// <summary>Result of an operation run through <see cref="IFaultBoundary"/>.</summary>
/// <typeparam name="T">Value type returned on success.</typeparam>
public sealed class GuardedResult<T>
{
    internal GuardedResult(GuardedResultKind kind, T? value, Exception? exception)
    {
        Kind = kind;
        Value = value;
        Exception = exception;
    }

    /// <summary>How the operation ended.</summary>
    public GuardedResultKind Kind { get; }

    /// <summary>The value when <see cref="Kind"/> is <see cref="GuardedResultKind.Succeeded"/>.</summary>
    public T? Value { get; }

    /// <summary>The exception when <see cref="Kind"/> is <see cref="GuardedResultKind.Faulted"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>True when the operation returned a value.</summary>
    public bool IsSucceeded => Kind == GuardedResultKind.Succeeded;
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

/// <summary>One captured log call with the scopes that were active, as App Insights would receive it.</summary>
public sealed record LogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyDictionary<string, object?> Scopes);

/// <summary>Shared sink so scopes opened by one logger are visible to entries written by another, like a real provider.</summary>
public sealed class LogSink
{
    private readonly AsyncLocal<ImmutableScopeStack?> _scopes = new();

    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public IReadOnlyList<LogEntry> ByEventId(int eventId) => Entries.Where(entry => entry.EventId.Id == eventId).ToArray();

    public LogEntry SingleEvent(int eventId) => ByEventId(eventId).Should().ContainSingle().Subject;

    internal IDisposable Push(object? state)
    {
        var parent = _scopes.Value;
        _scopes.Value = new ImmutableScopeStack(state, parent);
        return new ScopeHandle(() => _scopes.Value = parent);
    }

    internal IReadOnlyDictionary<string, object?> SnapshotScopes()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var scope = _scopes.Value; scope is not null; scope = scope.Parent)
        {
            if (scope.State is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var (key, value) in pairs)
                {
                    values.TryAdd(key, value);
                }
            }
        }

        return values;
    }

    internal sealed record ImmutableScopeStack(object? State, ImmutableScopeStack? Parent);

    private sealed class ScopeHandle(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>Logger that records every call so tests can assert event ids, levels, structured fields, and scopes.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public CapturingLogger(LogSink? sink = null)
    {
        Sink = sink ?? new LogSink();
    }

    public LogSink Sink { get; }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => Sink.Push(state);

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                fields[key] = value;
            }
        }

        Sink.Entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception, fields, Sink.SnapshotScopes()));
    }
}

/// <summary>Logger factory over one sink, for code that creates loggers by category.</summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    public LogSink Sink { get; } = new();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger<object>(Sink);

    public void Dispose()
    {
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

/// <summary>Landing store in memory. Hashes like the Blob store; can fail or corrupt per blob.</summary>
public sealed class FakeLandingStore : ILandingFileStore
{
    public ConcurrentDictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    public Exception? WriteFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public int Writes { get; private set; }

    public async Task<LandedFile> WriteAsync(string blobName, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken)
    {
        Writes++;
        using var buffer = new MemoryStream();
        await writeContent(buffer, cancellationToken);
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        var bytes = buffer.ToArray();
        Blobs[blobName] = bytes;
        return new LandedFile(blobName, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public Task<IFileContent> ReadAsync(string blobName, long expectedLength, CancellationToken cancellationToken)
        => ReadFailure is null
            ? Task.FromResult<IFileContent>(new FakeFileContent(Blobs[blobName]))
            : Task.FromException<IFileContent>(ReadFailure);

    public void Corrupt(string blobName) => Blobs[blobName] = TestData.Bytes("tampered");
}

/// <summary>Queue in memory: due messages are pumped by the test; scheduled ones wait for the fake clock.</summary>
public sealed class FakeWorkSender : IInboundWorkSender
{
    private readonly TimeProvider _time;
    private readonly List<(InboundWorkStage Stage, InboundFileWorkItem Item, DateTimeOffset DueUtc)> _messages = [];
    private readonly object _gate = new();

    public FakeWorkSender(TimeProvider time) => _time = time;

    public Exception? SendFailure { get; set; }

    public List<(InboundWorkStage Stage, InboundFileWorkItem Item, TimeSpan Delay)> Sent { get; } = [];

    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public Task SendAsync(InboundWorkStage stage, IReadOnlyList<InboundFileWorkItem> items, CancellationToken cancellationToken)
    {
        if (SendFailure is not null)
        {
            return Task.FromException(SendFailure);
        }

        lock (_gate)
        {
            foreach (var item in items)
            {
                _messages.Add((stage, item, _time.GetUtcNow()));
                Sent.Add((stage, item, TimeSpan.Zero));
            }
        }

        return Task.CompletedTask;
    }

    public Task ScheduleAsync(InboundWorkStage stage, InboundFileWorkItem item, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (SendFailure is not null)
        {
            return Task.FromException(SendFailure);
        }

        lock (_gate)
        {
            _messages.Add((stage, item, _time.GetUtcNow() + delay));
            Sent.Add((stage, item, delay));
        }

        return Task.CompletedTask;
    }

    public bool TryTakeDue(out (InboundWorkStage Stage, InboundFileWorkItem Item) message)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var index = _messages.FindIndex(pending => pending.DueUtc <= now);
            if (index < 0)
            {
                message = default;
                return false;
            }

            var taken = _messages[index];
            _messages.RemoveAt(index);
            message = (taken.Stage, taken.Item);
            return true;
        }
    }

    public void Duplicate(InboundWorkStage stage, InboundFileWorkItem item)
    {
        lock (_gate)
        {
            _messages.Add((stage, item, _time.GetUtcNow()));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }
}

/// <summary>Lease provider in memory; names in <see cref="HeldElsewhere"/> are refused.</summary>
public sealed class FakeRunLeaseProvider : IRunLeaseProvider
{
    public HashSet<string> HeldElsewhere { get; } = new(StringComparer.Ordinal);

    public List<string> Acquired { get; } = [];

    public int Released { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken)
    {
        if (HeldElsewhere.Contains(leaseName))
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Acquired.Add(leaseName);
        return Task.FromResult<IAsyncDisposable?>(new Lease(this));
    }

    private sealed class Lease(FakeRunLeaseProvider owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Manifest contexts over one open in-memory SQLite connection (the database lives as long as the connection).</summary>
public sealed class SqliteManifestDbContextFactory : IManifestDbContextFactory, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<InboundManifestDbContext> _options;

    public SqliteManifestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<InboundManifestDbContext>().UseSqlite(_connection).Options;
        using var context = new InboundManifestDbContext(_options);
        context.Database.EnsureCreated();
    }

    public Exception? Failure { get; set; }

    public Task<InboundManifestDbContext> CreateAsync(CancellationToken cancellationToken)
        => Failure is null ? Task.FromResult(new InboundManifestDbContext(_options)) : Task.FromException<InboundManifestDbContext>(Failure);

    public InboundManifestDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

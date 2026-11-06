using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

/// <summary>SFTP session over an in-memory folder with per-file fault injection.</summary>
public sealed class FakeSftpSession : ISftpSession
{
    private int _active;

    public List<RemoteFile> Files { get; } = [];

    public ConcurrentDictionary<string, byte[]> Contents { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Exception> DownloadFailures { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Exception> DeleteFailures { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<string> Deleted { get; } = [];

    public ConcurrentBag<FakeFileContent> Downloads { get; } = [];

    public Exception? ListFailure { get; set; }

    public Func<RemoteFile, CancellationToken, Task>? BeforeDownload { get; set; }

    public int MaxConcurrentDownloads { get; private set; }

    public bool IsDisposed { get; private set; }

    public FakeSftpSession With(RemoteFile file, byte[]? content = null)
    {
        Files.Add(file);
        Contents[file.Name] = content ?? TestData.Bytes($"ISA*{file.Name}~");
        return this;
    }

    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken cancellationToken)
        => ListFailure is null ? Task.FromResult<IReadOnlyList<RemoteFile>>(Files.ToArray()) : Task.FromException<IReadOnlyList<RemoteFile>>(ListFailure);

    public async Task<IFileContent> DownloadAsync(RemoteFile file, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _active);
        MaxConcurrentDownloads = Math.Max(MaxConcurrentDownloads, active);
        try
        {
            if (BeforeDownload is not null)
            {
                await BeforeDownload(file, cancellationToken);
            }

            await Task.Yield();
            if (DownloadFailures.TryGetValue(file.Name, out var failure))
            {
                throw failure;
            }

            var content = new FakeFileContent(Contents[file.Name]);
            Downloads.Add(content);
            return content;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public async Task CopyToAsync(RemoteFile file, Stream destination, CancellationToken cancellationToken)
    {
        if (BeforeDownload is not null)
        {
            await BeforeDownload(file, cancellationToken);
        }

        if (DownloadFailures.TryGetValue(file.Name, out var failure))
        {
            throw failure;
        }

        await destination.WriteAsync(Contents[file.Name], cancellationToken);
    }

    public Task DeleteAsync(RemoteFile file, CancellationToken cancellationToken)
    {
        if (DeleteFailures.TryGetValue(file.Name, out var failure))
        {
            return Task.FromException(failure);
        }

        Deleted.Add(file.Name);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Backup store that records uploads and can fail per file.</summary>
public sealed class FakeBackupStore : IBackupFileStore
{
    public ConcurrentDictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);

    public Task<BackupWriteResult> WriteOnceAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(fileName, out var failure))
        {
            return Task.FromException<BackupWriteResult>(failure);
        }

        Stored[fileName] = content.ToArray();
        return Task.FromResult(new BackupWriteResult(fileName, Existing.Contains(fileName)));
    }
}

/// <summary>Billing registrar that returns a configured registration per file (default: accepted and queued).</summary>
public sealed class FakeBillingRegistrar : IBillingFileRegistrar
{
    public ConcurrentDictionary<string, BillingRegistration> Responses { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, (int ClearingHouseId, string Title, byte[] Content)> Registered { get; } = new(StringComparer.Ordinal);

    public Task<BillingRegistration> RegisterAsync(ClearingHouseDescriptor clearingHouse, string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(fileName, out var failure))
        {
            return Task.FromException<BillingRegistration>(failure);
        }

        Registered[fileName] = (clearingHouse.Id, clearingHouse.Title, content.ToArray());
        return Task.FromResult(Responses.TryGetValue(fileName, out var response) ? response : BillingRegistration.Succeeded(100));
    }
}

/// <summary>Real application services wired over fakes, so tests exercise the same code path as production.</summary>
public sealed class DownloadPipeline
{
    public DownloadPipeline(Action<InboundDownloadOptions>? configure = null)
    {
        Options = TestData.Options(configure);
        FaultBoundary = new FaultBoundary(new CapturingLogger<FaultBoundary>(Sink));
        Formatter = new FailureMessageFormatter(Options);
        Processor = new InboundFileProcessor(Backup, Billing, FaultBoundary, Formatter, Options, new CapturingLogger<InboundFileProcessor>(Sink));
        BatchProcessor = new InboundFileBatchProcessor(Processor, FaultBoundary, Formatter, Options, new CapturingLogger<InboundFileBatchProcessor>(Sink));
        Selector = new RemoteFileSelector(Time, Options);
    }

    public LogSink Sink { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(TestData.BaseTimeUtc.AddHours(1)));

    public FakeSftpSession Session { get; } = new();

    public FakeBackupStore Backup { get; } = new();

    public FakeBillingRegistrar Billing { get; } = new();

    public Microsoft.Extensions.Options.IOptions<InboundDownloadOptions> Options { get; }

    public FaultBoundary FaultBoundary { get; }

    public FailureMessageFormatter Formatter { get; }

    public InboundFileProcessor Processor { get; }

    public InboundFileBatchProcessor BatchProcessor { get; }

    public RemoteFileSelector Selector { get; }
}

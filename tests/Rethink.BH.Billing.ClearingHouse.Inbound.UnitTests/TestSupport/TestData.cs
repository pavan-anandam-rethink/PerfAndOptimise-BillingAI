using System.Text;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

/// <summary>Synthetic, PHI-free test data shared across tests.</summary>
public static class TestData
{
    public static readonly ClearingHouseDescriptor Availity = new(1, "Availity");

    public static readonly ClearingHouseDescriptor Stedi = new(8, "Stedi");

    public static readonly DateTime BaseTimeUtc = new(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc);

    public static RemoteFile File(string name, int minutesAfterBase = 0, long size = 128)
        => new($"/ReceiveFiles/{name}", name, size, BaseTimeUtc.AddMinutes(minutesAfterBase));

    public static IOptions<InboundDownloadOptions> Options(Action<InboundDownloadOptions>? configure = null)
    {
        var options = new InboundDownloadOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static string SamplePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Samples", fileName);

    public static byte[] Sample(string fileName) => System.IO.File.ReadAllBytes(SamplePath(fileName));

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}

/// <summary>In-memory file content that records disposal.</summary>
public sealed class FakeFileContent : IFileContent
{
    private readonly byte[] _bytes;

    public FakeFileContent(byte[] bytes)
    {
        _bytes = bytes;
    }

    public bool IsDisposed { get; private set; }

    public ReadOnlyMemory<byte> Memory => IsDisposed ? throw new ObjectDisposedException(nameof(FakeFileContent)) : _bytes;

    public void Dispose() => IsDisposed = true;
}

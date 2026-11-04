using System.Buffers;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>
/// Streams the Billing <c>UploadEDIResponseFile</c> body without building it in memory.
/// The bytes are identical to <c>JsonSerializer.Serialize(DownloadSftpFileRequest)</c> with default options:
/// <c>{"clearingHouseId":1,"Data":"&lt;base64&gt;","FileName":"…","Title":"…"}</c>.
/// The legacy path held the file, its base64 string (UTF-16), and its UTF-8 copy at once; this holds only the file.
/// </summary>
public sealed class InboundFileJsonContent : HttpContent
{
    private const int InputBlockBytes = 48 * 1024;

    private static readonly byte[] DataPropertyMarker = Encoding.UTF8.GetBytes("\"Data\":\"");

    private readonly byte[] _prefix;
    private readonly byte[] _suffix;
    private readonly ReadOnlyMemory<byte> _fileContent;

    /// <summary>Creates the content. <paramref name="fileContent"/> must stay valid until the request completes.</summary>
    public InboundFileJsonContent(int clearingHouseId, string fileName, string title, ReadOnlyMemory<byte> fileContent)
    {
        (_prefix, _suffix) = BuildEnvelope(clearingHouseId, fileName, title);
        _fileContent = fileContent;
        Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.WebName };
    }

    /// <summary>Exact body length: envelope plus padded base64 length.</summary>
    public long BodyLength => _prefix.Length + (long)Base64.GetMaxEncodedToUtf8Length(_fileContent.Length) + _suffix.Length;

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(_prefix, cancellationToken).ConfigureAwait(false);
        await WriteBase64Async(stream, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(_suffix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = BodyLength;
        return true;
    }

    private async Task WriteBase64Async(Stream stream, CancellationToken cancellationToken)
    {
        var encoded = ArrayPool<byte>.Shared.Rent(Base64.GetMaxEncodedToUtf8Length(InputBlockBytes));
        using var returnBuffer = new PooledArrayReturn(encoded);
        for (var offset = 0; offset < _fileContent.Length; offset += InputBlockBytes)
        {
            var block = _fileContent.Slice(offset, Math.Min(InputBlockBytes, _fileContent.Length - offset));
            Base64.EncodeToUtf8(block.Span, encoded, out _, out var written, isFinalBlock: true);
            await stream.WriteAsync(encoded.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
        }
    }

    private static (byte[] Prefix, byte[] Suffix) BuildEnvelope(int clearingHouseId, string fileName, string title)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("clearingHouseId", clearingHouseId);
            writer.WriteBase64String("Data", ReadOnlySpan<byte>.Empty);
            writer.WriteString("FileName", fileName);
            writer.WriteString("Title", title);
            writer.WriteEndObject();
        }

        var envelope = buffer.WrittenSpan;
        var split = envelope.IndexOf(DataPropertyMarker) + DataPropertyMarker.Length;
        return (envelope[..split].ToArray(), envelope[split..].ToArray());
    }

    private sealed class PooledArrayReturn : IDisposable
    {
        private readonly byte[] _array;

        public PooledArrayReturn(byte[] array) => _array = array;

        public void Dispose() => ArrayPool<byte>.Shared.Return(_array);
    }
}

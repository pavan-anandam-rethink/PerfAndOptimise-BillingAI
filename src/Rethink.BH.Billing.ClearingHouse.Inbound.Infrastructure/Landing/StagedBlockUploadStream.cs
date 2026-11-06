using System.Buffers;
using System.Security.Cryptography;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>
/// Write-only stream that stages fixed-size blocks of a block blob while hashing every byte with SHA-256. Nothing becomes
/// visible until <see cref="CommitAsync"/>; a failed download leaves only uncommitted blocks, which Azure discards, so a
/// partial file is never readable. The staging buffer is rented from the shared pool and cleared before it is returned.
/// </summary>
public sealed class StagedBlockUploadStream : Stream
{
    private readonly BlockBlobStager _stager;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[] _buffer;
    private int _buffered;
    private long _length;
    private bool _disposed;

    /// <summary>Creates the stream over <paramref name="stager"/> with blocks of <paramref name="blockSize"/> bytes.</summary>
    public StagedBlockUploadStream(BlockBlobStager stager, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);

        _stager = stager;
        _buffer = ArrayPool<byte>.Shared.Rent(blockSize);
        BlockSize = blockSize;
    }

    /// <summary>Bytes per staged block.</summary>
    public int BlockSize { get; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_disposed;

    /// <inheritdoc />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    /// <summary>Stages the last block, commits the block list with the hash as metadata, and returns length and hash.</summary>
    public async Task<(long Length, string Sha256)> CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await StageBufferedAsync(cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(_hash.GetHashAndReset());
        await _stager.CommitAsync(sha256, cancellationToken).ConfigureAwait(false);
        return (_length, sha256);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hash.AppendData(buffer.Span);
        _length += buffer.Length;
        while (!buffer.IsEmpty)
        {
            var take = Math.Min(BlockSize - _buffered, buffer.Length);
            buffer.Span[..take].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += take;
            buffer = buffer[take..];
            if (_buffered == BlockSize)
            {
                await StageBufferedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Blocks are staged when full and on commit; there is nothing else to flush.
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = [];
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task StageBufferedAsync(CancellationToken cancellationToken)
    {
        if (_buffered == 0)
        {
            return;
        }

        await _stager.StageAsync(_buffer, _buffered, cancellationToken).ConfigureAwait(false);
        _buffered = 0;
    }
}

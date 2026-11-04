using System.Buffers;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>
/// Write-only stream backed by a buffer rented from <see cref="ArrayPool{T}.Shared"/>, sized from the listed file size
/// so a download normally needs one rental and no copy. On dispose the buffer is cleared before it goes back to the
/// pool, so EDI content (PHI) never leaks to the next renter.
/// </summary>
public sealed class PooledFileContentStream : Stream, IFileContent
{
    private const int MinimumCapacity = 4096;

    private byte[] _buffer;
    private int _length;
    private bool _disposed;

    /// <summary>Rents a buffer for at least <paramref name="expectedLength"/> bytes.</summary>
    public PooledFileContentStream(long expectedLength)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(ToCapacity(expectedLength));
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer.AsMemory(0, _length);
        }
    }

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

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity((long)_length + buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Nothing is buffered beyond the pooled array.
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
            _length = 0;
        }

        base.Dispose(disposing);
    }

    private void EnsureCapacity(long required)
    {
        if (required <= _buffer.Length)
        {
            return;
        }

        if (required > Array.MaxLength)
        {
            throw new InvalidOperationException($"File exceeds the maximum supported size of {Array.MaxLength} bytes.");
        }

        var grown = ArrayPool<byte>.Shared.Rent(ToCapacity(Math.Max(required, (long)_buffer.Length * 2)));
        _buffer.AsSpan(0, _length).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        _buffer = grown;
    }

    private static int ToCapacity(long requested) => (int)Math.Clamp(requested, MinimumCapacity, Array.MaxLength);
}

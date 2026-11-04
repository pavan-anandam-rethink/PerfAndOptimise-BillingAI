namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>
/// Downloaded file bytes. The memory may be rented from a pool, so it must not be used after <see cref="IDisposable.Dispose"/>.
/// </summary>
public interface IFileContent : IDisposable
{
    /// <summary>The file bytes.</summary>
    ReadOnlyMemory<byte> Memory { get; }
}

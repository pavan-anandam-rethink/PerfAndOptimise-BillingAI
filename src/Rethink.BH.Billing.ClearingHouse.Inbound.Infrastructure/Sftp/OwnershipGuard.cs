namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>
/// Disposes <typeparamref name="T"/> when the scope ends unless ownership was handed to the caller with <see cref="Release"/>.
/// Replaces a try/catch-dispose-rethrow around code that may fail before the resource is returned.
/// </summary>
/// <typeparam name="T">Owned resource.</typeparam>
public sealed class OwnershipGuard<T> : IDisposable
    where T : class, IDisposable
{
    private T? _resource;

    /// <summary>Takes ownership of <paramref name="resource"/>.</summary>
    public OwnershipGuard(T resource)
    {
        _resource = resource;
    }

    /// <summary>Hands the resource to the caller; the guard will no longer dispose it.</summary>
    public T Release()
    {
        var resource = _resource ?? throw new InvalidOperationException("Ownership was already released.");
        _resource = null;
        return resource;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _resource?.Dispose();
        _resource = null;
    }
}

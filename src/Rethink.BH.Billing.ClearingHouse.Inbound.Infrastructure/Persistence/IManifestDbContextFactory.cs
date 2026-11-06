namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>Creates a short-lived manifest context per operation. The caller disposes it.</summary>
public interface IManifestDbContextFactory
{
    /// <summary>Creates a context. The first call resolves the connection string (Key Vault when it is a secret name).</summary>
    Task<InboundManifestDbContext> CreateAsync(CancellationToken cancellationToken);
}

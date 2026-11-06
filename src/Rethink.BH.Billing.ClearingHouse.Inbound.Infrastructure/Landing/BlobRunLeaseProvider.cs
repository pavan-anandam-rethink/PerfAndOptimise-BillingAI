using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>
/// Run lock on a blob lease in the landing container (<c>leases/{name}</c>). Only one lister per clearing house holds it;
/// it is renewed in the background and released on dispose, and if the pod dies it expires on its own.
/// </summary>
public sealed class BlobRunLeaseProvider : IRunLeaseProvider
{
    private const int ConflictStatus = 409;
    private const string LeasePrefix = "leases/";

    private readonly LandingContainer _container;
    private readonly TimeSpan _leaseDuration;
    private readonly ILogger<BlobRunLeaseProvider> _logger;

    /// <summary>Creates the provider.</summary>
    public BlobRunLeaseProvider(LandingContainer container, IOptions<LandingStorageOptions> options, ILogger<BlobRunLeaseProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(options);

        _container = container;
        _leaseDuration = TimeSpan.FromSeconds(options.Value.LeaseSeconds);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        var container = await _container.Provider.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        var blob = container.GetBlobClient(LeasePrefix + leaseName);
        await EnsureLeaseBlobAsync(blob, cancellationToken).ConfigureAwait(false);

        var leaseClient = blob.GetBlobLeaseClient();
        var acquired = await TryAcquireLeaseAsync(leaseClient, _leaseDuration, cancellationToken).ConfigureAwait(false);
        return acquired ? BlobRunLease.Start(new AzureLeaseHandle(leaseClient), leaseName, _leaseDuration, _logger) : null;
    }

    private static async Task<bool> TryAcquireLeaseAsync(BlobLeaseClient leaseClient, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await leaseClient.AcquireAsync(duration, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException held) when (held.Status == ConflictStatus)
        {
            return false;
        }
    }

    private static async Task EnsureLeaseBlobAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        if (await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await blob.UploadAsync(BinaryData.Empty, overwrite: false, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException created) when (created.Status == ConflictStatus)
        {
            // Another lister created the blob between the check and the upload; the lease decides who runs.
        }
    }

    private sealed class AzureLeaseHandle : IRunLeaseHandle
    {
        private readonly BlobLeaseClient _client;

        public AzureLeaseHandle(BlobLeaseClient client) => _client = client;

        public Task RenewAsync(CancellationToken cancellationToken) => _client.RenewAsync(cancellationToken: cancellationToken);

        public Task ReleaseAsync(CancellationToken cancellationToken) => _client.ReleaseAsync(cancellationToken: cancellationToken);
    }
}

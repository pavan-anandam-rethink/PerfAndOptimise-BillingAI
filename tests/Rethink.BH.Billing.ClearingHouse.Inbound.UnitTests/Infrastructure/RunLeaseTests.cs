using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Moq.Protected;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class RunLeaseTests
{
    private const string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net";

    private readonly Mock<BlobClient> _blob = new();
    private readonly Mock<BlobLeaseClient> _leaseClient = new();
    private readonly Mock<BlobContainerClient> _container = new();
    private readonly LogSink _sink = new();

    public RunLeaseTests()
    {
        _container.Setup(container => container.GetBlobClient("leases/inbound-lister-1")).Returns(_blob.Object);
        _blob.Protected().Setup<BlobLeaseClient>("GetBlobLeaseClientCore", ItExpr.IsAny<string>()).Returns(_leaseClient.Object);
        _leaseClient.Setup(lease => lease.ReleaseAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<Response<ReleasedObjectInfo>>());
        _leaseClient.Setup(lease => lease.RenewAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<Response<BlobLease>>());
    }

    [Fact]
    public async Task Creates_the_lease_blob_once_and_acquires_the_lease()
    {
        SetupExists(false);
        _blob.Setup(blob => blob.UploadAsync(It.IsAny<BinaryData>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        SetupAcquire();

        var lease = await Provider().TryAcquireAsync("inbound-lister-1", CancellationToken.None);
        await lease!.DisposeAsync();
        await lease.DisposeAsync();

        lease.Should().BeOfType<BlobRunLease>();
        _leaseClient.Verify(l => l.AcquireAsync(TimeSpan.FromSeconds(15), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        _leaseClient.Verify(l => l.ReleaseAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Blob_created_by_another_lister_meanwhile_is_fine()
    {
        SetupExists(false);
        _blob.Setup(blob => blob.UploadAsync(It.IsAny<BinaryData>(), false, It.IsAny<CancellationToken>())).ThrowsAsync(new RequestFailedException(409, "BlobAlreadyExists"));
        SetupAcquire();

        var lease = await Provider().TryAcquireAsync("inbound-lister-1", CancellationToken.None);

        lease.Should().NotBeNull();
        await lease!.DisposeAsync();
    }

    [Fact]
    public async Task Held_lease_returns_null()
    {
        SetupExists(true);
        _leaseClient
            .Setup(lease => lease.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(409, "LeaseAlreadyPresent"));

        var lease = await Provider().TryAcquireAsync("inbound-lister-1", CancellationToken.None);

        lease.Should().BeNull();
        _blob.Verify(blob => blob.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Other_storage_errors_propagate()
    {
        SetupExists(true);
        _leaseClient
            .Setup(lease => lease.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "AuthorizationFailure"));

        var act = () => Provider().TryAcquireAsync("inbound-lister-1", CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
        await FluentActions.Awaiting(() => Provider().TryAcquireAsync(" ", CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Lease_renews_in_the_background_until_disposed()
    {
        var handle = new FakeLeaseHandle();
        var lease = BlobRunLease.Start(handle, "inbound-lister-1", TimeSpan.FromMilliseconds(60), new CapturingLogger<BlobRunLease>(_sink));

        await handle.WaitForRenewalsAsync(2);
        await lease.DisposeAsync();

        handle.Renewals.Should().BeGreaterThanOrEqualTo(2);
        handle.Released.Should().Be(1);
    }

    [Fact]
    public async Task Renewal_failure_is_logged_and_renewal_stops()
    {
        var handle = new FakeLeaseHandle { RenewFailure = new RequestFailedException(409, "LeaseLost") };
        var lease = BlobRunLease.Start(handle, "inbound-lister-1", TimeSpan.FromMilliseconds(30), new CapturingLogger<BlobRunLease>(_sink));

        await handle.WaitForRenewalsAsync(1);
        await Task.Delay(50);
        await lease.DisposeAsync();

        handle.Renewals.Should().Be(1);
        _sink.SingleEvent(2021).Exception.Should().BeOfType<RequestFailedException>();
    }

    [Fact]
    public async Task Release_failure_is_logged_not_thrown()
    {
        var handle = new FakeLeaseHandle { ReleaseFailure = new RequestFailedException(409, "LeaseIdMismatch") };
        var lease = BlobRunLease.Start(handle, "inbound-lister-1", TimeSpan.FromSeconds(60), new CapturingLogger<BlobRunLease>(_sink));

        await lease.DisposeAsync();

        _sink.SingleEvent(2022).State["LeaseSeconds"].Should().Be(60);
        FluentActions.Invoking(() => BlobRunLease.Start(null!, "x", TimeSpan.FromSeconds(1), new CapturingLogger<BlobRunLease>())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => BlobRunLease.Start(handle, "x", TimeSpan.FromSeconds(1), null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Provider_validates_its_arguments()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions());

        FluentActions.Invoking(() => new BlobRunLeaseProvider(null!, options, new CapturingLogger<BlobRunLeaseProvider>())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BlobRunLeaseProvider(Container(), null!, new CapturingLogger<BlobRunLeaseProvider>())).Should().Throw<ArgumentNullException>();
    }

    private BlobRunLeaseProvider Provider()
        => new(Container(), Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions { LeaseSeconds = 15 }), new CapturingLogger<BlobRunLeaseProvider>(_sink));

    private LandingContainer Container()
    {
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(_container.Object);
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionString);
        return new LandingContainer(
            Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()),
            Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions { ContainerName = "c", ConnectionString = ConnectionString }),
            resolver.Object,
            _ => service.Object);
    }

    private void SetupExists(bool exists)
        => _blob.Setup(blob => blob.ExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));

    private void SetupAcquire()
        => _leaseClient
            .Setup(lease => lease.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobLease>>());

    private sealed class FakeLeaseHandle : IRunLeaseHandle
    {
        private readonly TaskCompletionSource _renewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _renewals;
        private int _target = 1;

        public Exception? RenewFailure { get; init; }

        public Exception? ReleaseFailure { get; init; }

        public int Renewals => Volatile.Read(ref _renewals);

        public int Released { get; private set; }

        public Task RenewAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _renewals) >= Volatile.Read(ref _target))
            {
                _renewed.TrySetResult();
            }

            return RenewFailure is null ? Task.CompletedTask : Task.FromException(RenewFailure);
        }

        public Task ReleaseAsync(CancellationToken cancellationToken)
        {
            Released++;
            return ReleaseFailure is null ? Task.CompletedTask : Task.FromException(ReleaseFailure);
        }

        public Task WaitForRenewalsAsync(int count)
        {
            Volatile.Write(ref _target, count);
            if (Renewals >= count)
            {
                _renewed.TrySetResult();
            }

            return _renewed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

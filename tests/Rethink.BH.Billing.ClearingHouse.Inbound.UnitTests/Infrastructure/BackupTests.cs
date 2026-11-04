using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class BackupTests
{
    private const string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net";

    private readonly Mock<BlobContainerClient> _container = new();
    private readonly Mock<BlobClient> _blob = new();

    public BackupTests()
    {
        _container.Setup(container => container.GetBlobClient(It.IsAny<string>())).Returns(_blob.Object);
    }

    [Fact]
    public async Task Store_uploads_new_files_from_the_buffer_with_overwrite()
    {
        var content = TestData.Sample("AV_999_20261104_0001.x12");
        BinaryData? uploaded = null;
        SetupExists(false);
        _blob
            .Setup(blob => blob.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
            .Callback((BinaryData data, bool _, CancellationToken _) => uploaded = data)
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        var store = new AzureBlobBackupFileStore(Provider());

        var result = await store.WriteOnceAsync("AV_999_20261104_0001.x12", content, CancellationToken.None);

        result.Should().Be(new BackupWriteResult("AV_999_20261104_0001.x12", AlreadyExisted: false));
        uploaded!.ToArray().Should().Equal(content);
        _container.Verify(container => container.GetBlobClient("AV_999_20261104_0001.x12"), Times.Once);
        _blob.Verify(blob => blob.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Store_leaves_existing_backups_unchanged()
    {
        SetupExists(true);
        var store = new AzureBlobBackupFileStore(Provider());

        var result = await store.WriteOnceAsync("dup.x12", new byte[3], CancellationToken.None);

        result.AlreadyExisted.Should().BeTrue();
        result.BlobPath.Should().Be("dup.x12");
        _blob.Verify(blob => blob.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Store_requires_a_file_name(string? fileName)
    {
        var provider = new Mock<IBackupContainerProvider>(MockBehavior.Strict);
        var store = new AzureBlobBackupFileStore(provider.Object);

        var act = () => store.WriteOnceAsync(fileName!, new byte[1], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("File name is required.");
    }

    [Fact]
    public async Task Container_provider_resolves_the_connection_once_under_concurrency()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync("blob-secret", BackupStorageOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionString);
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient("availitybackup")).Returns(_container.Object);
        var created = 0;
        using var provider = new BackupContainerProvider(
            StorageOptions("availitybackup", "blob-secret"),
            resolver.Object,
            connection =>
            {
                Interlocked.Increment(ref created);
                connection.Should().Be(ConnectionString);
                return service.Object;
            });

        var containers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetContainerAsync(CancellationToken.None).AsTask()));

        containers.Should().AllSatisfy(container => container.Should().BeSameAs(_container.Object));
        created.Should().Be(1);
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_provider_does_not_cache_failures()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .SetupSequence(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vault down"))
            .ReturnsAsync(ConnectionString);
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(_container.Object);
        using var provider = new BackupContainerProvider(StorageOptions("c", "secret"), resolver.Object, _ => service.Object);

        await provider.Invoking(p => p.GetContainerAsync(CancellationToken.None).AsTask()).Should().ThrowAsync<InvalidOperationException>().WithMessage("vault down");
        (await provider.GetContainerAsync(CancellationToken.None)).Should().BeSameAs(_container.Object);
    }

    [Fact]
    public async Task Container_provider_default_factory_builds_a_real_client_without_network()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? value, string _, ISecretReferencePolicy _, CancellationToken _) => value!);
        using var provider = new BackupContainerProvider(StorageOptions("availitybackup", ConnectionString), resolver.Object);

        var container = await provider.GetContainerAsync(CancellationToken.None);

        container.Name.Should().Be("availitybackup");
        container.AccountName.Should().Be("test");
        FluentActions.Invoking(() => new BackupContainerProvider(null!, resolver.Object)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Container_provider_honours_cancellation_while_waiting()
    {
        using var provider = new BackupContainerProvider(StorageOptions("c", "s"), Mock.Of<ISecretResolver>(), _ => Mock.Of<BlobServiceClient>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await provider.Invoking(p => p.GetContainerAsync(cancelled.Token).AsTask()).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Storage_options_read_the_primary_connection()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["AvailityBackup"] = "availitybackup",
            ["ConnectionStrings:BlobStorage:ConnectionString"] = "primary-secret",
            ["EligibilityProcessing:Blob:ConnectionString"] = "fallback-secret",
        });

        options.ContainerName.Should().Be("availitybackup");
        options.ConnectionString.Should().Be("primary-secret");
        options.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Storage_options_fall_back_to_the_eligibility_connection()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["AvailityBackup"] = "availitybackup",
            ["ConnectionStrings:BlobStorage:ConnectionString"] = " ",
            ["EligibilityProcessing:Blob:ConnectionString"] = "fallback-secret",
        });

        options.ConnectionString.Should().Be("fallback-secret");
    }

    [Fact]
    public void Storage_options_are_invalid_when_values_are_missing()
    {
        var options = Configure([]);

        options.ContainerName.Should().BeEmpty();
        options.ConnectionString.Should().BeEmpty();
        options.IsValid.Should().BeFalse();
        new BackupStorageOptions { ContainerName = "c" }.IsValid.Should().BeFalse();
        FluentActions.Invoking(() => new BackupStorageOptions().Configure(null!)).Should().Throw<ArgumentNullException>();
    }

    private static BackupStorageOptions Configure(Dictionary<string, string?> values)
    {
        var options = new BackupStorageOptions();
        options.Configure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return options;
    }

    private static Microsoft.Extensions.Options.IOptions<BackupStorageOptions> StorageOptions(string container, string connection)
        => Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions { ContainerName = container, ConnectionString = connection });

    private IBackupContainerProvider Provider()
    {
        var provider = new Mock<IBackupContainerProvider>();
        provider.Setup(p => p.GetContainerAsync(It.IsAny<CancellationToken>())).Returns(() => new ValueTask<BlobContainerClient>(_container.Object));
        return provider.Object;
    }

    private void SetupExists(bool exists)
        => _blob
            .Setup(blob => blob.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));
}

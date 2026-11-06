using System.Security.Cryptography;
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

public sealed class LandingTests
{
    private const string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net";

    private readonly Mock<BlockBlobClient> _blockBlob = new();
    private readonly Mock<BlobClient> _blob = new();
    private readonly Mock<BlobContainerClient> _container = new();
    private readonly List<(string Id, byte[] Bytes)> _staged = [];
    private IEnumerable<string>? _committed;
    private CommitBlockListOptions? _commitOptions;

    public LandingTests()
    {
        _blockBlob
            .Setup(blob => blob.StageBlockAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<BlockBlobStageBlockOptions>(), It.IsAny<CancellationToken>()))
            .Callback((string id, Stream content, BlockBlobStageBlockOptions _, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                _staged.Add((id, copy.ToArray()));
            })
            .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
        _blockBlob
            .Setup(blob => blob.CommitBlockListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CommitBlockListOptions>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<string> ids, CommitBlockListOptions options, CancellationToken _) =>
            {
                _committed = ids.ToArray();
                _commitOptions = options;
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        _container.Protected().Setup<BlockBlobClient>("GetBlockBlobClientCore", ItExpr.IsAny<string>()).Returns(_blockBlob.Object);
        _container.Setup(container => container.GetBlobClient(It.IsAny<string>())).Returns(_blob.Object);
    }

    [Fact]
    public async Task Upload_stream_stages_fixed_blocks_hashes_and_commits_in_order()
    {
        var content = Enumerable.Range(0, 25).Select(i => (byte)i).ToArray();
        await using var stream = new StagedBlockUploadStream(new BlockBlobStager(_blockBlob.Object), 10);

        await stream.WriteAsync(content.AsMemory(0, 7));
        stream.Write(content, 7, 8);
        await stream.WriteAsync(content, 15, 10, CancellationToken.None);
        var (length, sha256) = await stream.CommitAsync(CancellationToken.None);

        length.Should().Be(25);
        stream.Length.Should().Be(25);
        stream.Position.Should().Be(25);
        sha256.Should().Be(Convert.ToHexString(SHA256.HashData(content)));
        _staged.Select(block => block.Bytes.Length).Should().Equal(10, 10, 5);
        _staged.SelectMany(block => block.Bytes).Should().Equal(content);
        _committed.Should().Equal(_staged.Select(block => block.Id));
        _commitOptions!.Metadata[BlockBlobStager.Sha256MetadataKey].Should().Be(sha256);
        _commitOptions.HttpHeaders.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Empty_upload_commits_an_empty_blob()
    {
        await using var stream = new StagedBlockUploadStream(new BlockBlobStager(_blockBlob.Object), 10);

        var (length, sha256) = await stream.CommitAsync(CancellationToken.None);

        length.Should().Be(0);
        sha256.Should().Be(Convert.ToHexString(SHA256.HashData([])));
        _staged.Should().BeEmpty();
        _committed.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_stream_is_write_only_and_unusable_after_dispose()
    {
        var stream = new StagedBlockUploadStream(new BlockBlobStager(_blockBlob.Object), 4);

        stream.CanRead.Should().BeFalse();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeTrue();
        stream.BlockSize.Should().Be(4);
        stream.Flush();
        await stream.FlushAsync(CancellationToken.None);
        FluentActions.Invoking(() => stream.Read(new byte[1], 0, 1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.SetLength(1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Position = 1).Should().Throw<NotSupportedException>();
        await stream.DisposeAsync();
        stream.Dispose();

        stream.CanWrite.Should().BeFalse();
        await FluentActions.Awaiting(() => stream.WriteAsync(new byte[1]).AsTask()).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => stream.CommitAsync(CancellationToken.None)).Should().ThrowAsync<ObjectDisposedException>();
        FluentActions.Invoking(() => new StagedBlockUploadStream(new BlockBlobStager(_blockBlob.Object), 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Landing_store_streams_into_the_named_blob_and_returns_the_hash()
    {
        var store = new AzureBlobLandingFileStore(Container(), Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions { BlockSizeKiB = 256 }));
        var content = TestData.Bytes("ISA*landing~");

        var landed = await store.WriteAsync("1/2026/11/04/abc/a.835", (stream, token) => stream.WriteAsync(content, token).AsTask(), CancellationToken.None);

        landed.Should().Be(new Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline.LandedFile("1/2026/11/04/abc/a.835", content.Length, Convert.ToHexString(SHA256.HashData(content))));
        _container.Protected().Verify<BlockBlobClient>("GetBlockBlobClientCore", Times.Once(), "1/2026/11/04/abc/a.835");
    }

    [Fact]
    public async Task Failed_download_never_commits_the_landing_blob()
    {
        var store = new AzureBlobLandingFileStore(Container(), Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()));

        var act = () => store.WriteAsync("x", (_, _) => throw new IOException("sftp reset"), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        _committed.Should().BeNull();
    }

    [Fact]
    public async Task Landing_store_reads_into_pooled_content()
    {
        var content = TestData.Bytes("ISA*read~");
        _blob
            .Setup(blob => blob.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback((Stream destination, CancellationToken _) => destination.Write(content))
            .ReturnsAsync(Mock.Of<Response>());
        var store = new AzureBlobLandingFileStore(Container(), Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()));

        using var read = await store.ReadAsync("x", content.Length, CancellationToken.None);

        read.Memory.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task Landing_store_validates_arguments()
    {
        var store = new AzureBlobLandingFileStore(Container(), Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()));

        await FluentActions.Awaiting(() => store.WriteAsync(" ", (_, _) => Task.CompletedTask, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => store.WriteAsync("x", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => store.ReadAsync(" ", 1, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        FluentActions.Invoking(() => new AzureBlobLandingFileStore(null!, Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new AzureBlobLandingFileStore(Container(), null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Landing_container_uses_the_landing_name_on_the_backup_account()
    {
        var resolver = Resolver();
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient("inbound-landing")).Returns(_container.Object);
        using var landing = new LandingContainer(
            Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()),
            Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions { ContainerName = "availitybackup", ConnectionString = "blob-secret" }),
            resolver.Object,
            connection => connection == ConnectionString ? service.Object : throw new InvalidOperationException());

        var first = await landing.Provider.GetContainerAsync(CancellationToken.None);
        var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => landing.Provider.GetContainerAsync(CancellationToken.None).AsTask()));

        first.Should().BeSameAs(_container.Object);
        all.Should().OnlyContain(container => ReferenceEquals(container, _container.Object));
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Landing_container_default_factory_builds_a_real_client_lazily()
    {
        using var landing = new LandingContainer(
            Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()),
            Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions { ContainerName = "c", ConnectionString = ConnectionString }),
            Resolver().Object);

        landing.Provider.Should().NotBeNull();
        FluentActions.Invoking(() => new LandingContainer(null!, Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions()), Resolver().Object)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new LandingContainer(Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()), null!, Resolver().Object)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Container_provider_does_not_cache_a_failed_resolution()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .SetupSequence(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vault down"))
            .ReturnsAsync(ConnectionString);
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient("c")).Returns(_container.Object);
        using var provider = new Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Storage.BlobContainerProvider("c", "secret", "key", resolver.Object, _ => service.Object);

        await FluentActions.Awaiting(() => provider.GetContainerAsync(CancellationToken.None).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        (await provider.GetContainerAsync(CancellationToken.None)).Should().BeSameAs(_container.Object);
    }

    private LandingContainer Container()
    {
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(_container.Object);
        return new LandingContainer(
            Microsoft.Extensions.Options.Options.Create(new LandingStorageOptions()),
            Microsoft.Extensions.Options.Options.Create(new BackupStorageOptions { ContainerName = "c", ConnectionString = ConnectionString }),
            Resolver().Object,
            _ => service.Object);
    }

    private static Mock<ISecretResolver> Resolver()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), BackupStorageOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionString);
        return resolver;
    }
}

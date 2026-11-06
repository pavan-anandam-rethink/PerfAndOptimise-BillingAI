using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class SftpTests
{
    private static readonly SftpCredentials Credentials = new("sftp.example.test", 22, "user", "secret", "/ReceiveFiles/", null);

    private readonly CapturingLogger<SshNetSftpSession> _logger = new();
    private readonly List<Mock<ISftpClient>> _clients = [];
    private readonly Mock<ISftpClientFactory> _clientFactory = new();

    public SftpTests()
    {
        _clientFactory
            .Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                var client = NewClient();
                _clients.Add(client);
                return client.Object;
            });
    }

    [Fact]
    public async Task Open_connects_exactly_one_client_lazily()
    {
        using var session = await Open(poolSize: 4);

        session.CreatedClientCount.Should().Be(1);
        _clients.Should().ContainSingle();
        _clients[0].Verify(client => client.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        _clientFactory.Verify(factory => factory.Create(Credentials, TimeSpan.FromSeconds(30)), Times.Once);
    }

    [Fact]
    public async Task Open_disposes_the_client_when_connect_fails()
    {
        var failing = new Mock<ISftpClient>();
        failing.Setup(client => client.ConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new SshConnectionException("refused"));
        _clientFactory.Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>())).Returns(failing.Object);

        var act = () => Open();

        await act.Should().ThrowAsync<SshConnectionException>();
        failing.Verify(client => client.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Open_validates_arguments()
    {
        await FluentActions.Invoking(() => SshNetSftpSession.OpenAsync(_clientFactory.Object, null!, 1, TimeSpan.FromSeconds(1), _logger, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => SshNetSftpSession.OpenAsync(_clientFactory.Object, Credentials, 0, TimeSpan.FromSeconds(1), _logger, CancellationToken.None)).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task List_skips_directories_and_dot_files_and_trims_the_trailing_slash()
    {
        using var session = await Open();
        var modified = TestData.BaseTimeUtc;
        _clients[0]
            .Setup(client => client.ListDirectoryAsync("/ReceiveFiles", It.IsAny<CancellationToken>()))
            .Returns(Entries(
                Entry("AV_999.x12", 120, modified),
                Entry(".", 0, modified, isDirectory: true),
                Entry("archive", 0, modified, isDirectory: true),
                Entry(".hidden", 5, modified)));

        var files = await session.ListAsync(CancellationToken.None);

        files.Should().ContainSingle().Which.Should().Be(new RemoteFile("/ReceiveFiles/AV_999.x12", "AV_999.x12", 120, modified));
        _logger.Sink.SingleEvent(2002).State["FileCount"].Should().Be(1);
    }

    [Fact]
    public async Task Download_streams_into_pooled_content()
    {
        using var session = await Open();
        var payload = TestData.Sample("AV_835_20261104_0003.x12");
        _clients[0]
            .Setup(client => client.DownloadFileAsync("/ReceiveFiles/a.x12", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Stream output, CancellationToken token) => output.WriteAsync(payload, token).AsTask());

        using var content = await session.DownloadAsync(TestData.File("a.x12", size: payload.Length), CancellationToken.None);

        content.Memory.ToArray().Should().Equal(payload);
        var downloaded = _logger.Sink.SingleEvent(2003);
        downloaded.Level.Should().Be(LogLevel.Debug);
        downloaded.State["Bytes"].Should().Be((long)payload.Length);
    }

    [Fact]
    public async Task Copy_streams_straight_into_the_destination()
    {
        using var session = await Open();
        var payload = TestData.Sample("AV_835_20261104_0003.x12");
        _clients[0]
            .Setup(client => client.DownloadFileAsync("/ReceiveFiles/a.x12", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Stream output, CancellationToken token) => output.WriteAsync(payload, token).AsTask());
        using var destination = new MemoryStream();

        await session.CopyToAsync(TestData.File("a.x12", size: payload.Length), destination, CancellationToken.None);

        destination.ToArray().Should().Equal(payload);
        _logger.Sink.SingleEvent(2003).Level.Should().Be(LogLevel.Debug);
        await FluentActions.Invoking(() => session.CopyToAsync(null!, destination, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => session.CopyToAsync(TestData.File("a.x12"), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Download_failure_releases_the_slot()
    {
        using var session = await Open(poolSize: 1);
        _clients[0]
            .Setup(client => client.DownloadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SftpPathNotFoundException("gone"));

        await FluentActions.Invoking(() => session.DownloadAsync(TestData.File("a.x12"), CancellationToken.None)).Should().ThrowAsync<SftpPathNotFoundException>();
        _clients[0].Setup(client => client.DeleteFileAsync("/ReceiveFiles/a.x12", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await session.DeleteAsync(TestData.File("a.x12"), CancellationToken.None);

        _clients[0].Verify(client => client.DeleteFileAsync("/ReceiveFiles/a.x12", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pool_grows_only_under_concurrency_and_never_beyond_its_size()
    {
        var release = new TaskCompletionSource();
        _clientFactory
            .Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                var client = NewClient();
                client.Setup(c => c.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => release.Task);
                _clients.Add(client);
                return client.Object;
            });
        using var session = await Open(poolSize: 2);

        var deletes = Enumerable.Range(0, 3).Select(index => session.DeleteAsync(TestData.File($"f{index}.x12"), CancellationToken.None)).ToArray();
        await Task.Delay(50);
        session.CreatedClientCount.Should().Be(2);
        deletes.Should().OnlyContain(task => !task.IsCompleted);

        release.SetResult();
        await Task.WhenAll(deletes).WaitAsync(TimeSpan.FromSeconds(5));

        session.CreatedClientCount.Should().Be(2);
        _clients.Sum(client => client.Invocations.Count(call => call.Method.Name == nameof(ISftpClient.DeleteFileAsync))).Should().Be(3);
    }

    [Fact]
    public async Task Client_creation_failure_frees_the_slot()
    {
        var busy = new TaskCompletionSource();
        _clientFactory
            .Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                var client = NewClient();
                client.Setup(c => c.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => busy.Task);
                client.Setup(c => c.ListDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => Entries());
                _clients.Add(client);
                return client.Object;
            });
        using var session = await Open(poolSize: 2);
        var held = session.DeleteAsync(TestData.File("held.x12"), CancellationToken.None);
        _clientFactory.Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>())).Throws(new InvalidOperationException("no client"));

        await FluentActions.Invoking(() => session.ListAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();

        _clientFactory
            .Setup(factory => factory.Create(It.IsAny<SftpCredentials>(), It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                var client = NewClient();
                client.Setup(c => c.ListDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => Entries());
                _clients.Add(client);
                return client.Object;
            });
        var files = await session.ListAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        busy.SetResult();
        await held;

        files.Should().BeEmpty();
        session.CreatedClientCount.Should().Be(2);
    }

    [Fact]
    public async Task Connect_failure_on_a_pooled_client_frees_the_slot_and_reconnects_next_time()
    {
        using var session = await Open(poolSize: 1);
        _clients[0].Setup(client => client.IsConnected).Returns(false);
        _clients[0].Setup(client => client.ConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new SocketException());

        await FluentActions.Invoking(() => session.ListAsync(CancellationToken.None)).Should().ThrowAsync<SocketException>();

        _clients[0].Setup(client => client.ConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _clients[0].Setup(client => client.ListDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Entries());
        var files = await session.ListAsync(CancellationToken.None);
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_disposes_every_client_once_and_blocks_further_use()
    {
        var session = await Open();

        await session.DisposeAsync();
        session.Dispose();

        _clients[0].Verify(client => client.Dispose(), Times.Once);
        await FluentActions.Invoking(() => session.ListAsync(CancellationToken.None)).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Invoking(() => session.DownloadAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => session.DeleteAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "anything", true)]
    [InlineData("", "anything", true)]
    [InlineData("SHA256:abc123", "abc123", true)]
    [InlineData("sha256:abc123==", "abc123", true)]
    [InlineData("abc123", "abc123=", true)]
    [InlineData("SHA256:abc123", "zzz", false)]
    [InlineData("SHA256:abc123", "", false)]
    [InlineData("SHA256:abc123", null, false)]
    public void Host_key_verifier_compares_sha256_fingerprints(string? pinned, string? presented, bool trusted)
    {
        HostKeyVerifier.IsTrusted(presented, pinned).Should().Be(trusted);
    }

    [Fact]
    public void Client_factory_builds_a_password_client_and_logs_rejected_host_keys()
    {
        var logger = new CapturingLogger<SshNetSftpClientFactory>();
        var factory = new SshNetSftpClientFactory(logger);
        var pinned = new SftpCredentials("sftp.example.test", 2222, "user", "secret", "/in", "SHA256:expected");

        using var client = (SftpClient)factory.Create(pinned, TimeSpan.FromSeconds(7));

        client.ConnectionInfo.Host.Should().Be("sftp.example.test");
        client.ConnectionInfo.Port.Should().Be(2222);
        client.ConnectionInfo.Username.Should().Be("user");
        client.ConnectionInfo.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        client.ConnectionInfo.AuthenticationMethods.Should().ContainSingle().Which.Should().BeOfType<PasswordAuthenticationMethod>();
        factory.ShouldTrust(pinned, "expected").Should().BeTrue();
        logger.Sink.Entries.Should().BeEmpty();
        factory.ShouldTrust(pinned, "attacker").Should().BeFalse();
        var rejected = logger.Sink.SingleEvent(2006);
        rejected.Level.Should().Be(LogLevel.Error);
        rejected.State["PresentedFingerprint"].Should().Be("attacker");
        FluentActions.Invoking(() => factory.Create(null!, TimeSpan.Zero)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Pooled_content_grows_and_clears_on_dispose()
    {
        var content = new PooledFileContentStream(expectedLength: 0);
        var first = new byte[3000];
        var second = new byte[5000];
        Array.Fill(first, (byte)1);
        Array.Fill(second, (byte)2);

        content.Write(first, 0, first.Length);
        content.Write(second);

        content.Length.Should().Be(8000);
        content.Position.Should().Be(8000);
        content.Memory.Length.Should().Be(8000);
        content.Memory.Span[2999].Should().Be(1);
        content.Memory.Span[3000].Should().Be(2);
        content.CanWrite.Should().BeTrue();
        content.CanRead.Should().BeFalse();
        content.CanSeek.Should().BeFalse();

        content.Dispose();
        content.Dispose();

        content.CanWrite.Should().BeFalse();
        content.Length.Should().Be(0);
        FluentActions.Invoking(() => content.Memory).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => content.Write([1])).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Pooled_content_supports_async_writes_and_rejects_reads()
    {
        using var content = new PooledFileContentStream(expectedLength: 10);

        await content.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, CancellationToken.None);
        await content.WriteAsync(new ReadOnlyMemory<byte>([4, 5]), CancellationToken.None);
        content.Flush();
        await content.FlushAsync(CancellationToken.None);

        content.Memory.ToArray().Should().Equal(1, 2, 3, 4, 5);
        FluentActions.Invoking(() => content.Read(new byte[1], 0, 1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => content.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => content.SetLength(1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => content.Position = 0).Should().Throw<NotSupportedException>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await FluentActions.Invoking(() => content.WriteAsync(new byte[1], 0, 1, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => content.WriteAsync(new ReadOnlyMemory<byte>([1]), cancelled.Token).AsTask()).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Ownership_guard_disposes_unless_released()
    {
        var owned = new Mock<IDisposable>();
        using (new OwnershipGuard<IDisposable>(owned.Object))
        {
            owned.Verify(resource => resource.Dispose(), Times.Never);
        }

        owned.Verify(resource => resource.Dispose(), Times.Once);

        var released = new Mock<IDisposable>();
        var guard = new OwnershipGuard<IDisposable>(released.Object);
        guard.Release().Should().BeSameAs(released.Object);
        guard.Dispose();
        released.Verify(resource => resource.Dispose(), Times.Never);
        FluentActions.Invoking(() => guard.Release()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Retry_pipelines_retry_transient_faults_only()
    {
        var logger = new CapturingLogger<SftpRetryPipelines>();
        var pipelines = new SftpRetryPipelines(TestData.Options(options => { options.SftpRetryCount = 2; options.SftpRetryBaseDelayMilliseconds = 1; }), TimeProvider.System, logger);
        var attempts = 0;

        var result = await pipelines.List.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3 ? throw new SshConnectionException("dropped") : ValueTask.FromResult(7);
        });

        result.Should().Be(7);
        attempts.Should().Be(3);
        logger.Sink.ByEventId(2004).Should().HaveCount(2);
        logger.Sink.ByEventId(2004)[0].State["OperationName"].Should().Be("List");
        logger.Sink.ByEventId(2004)[0].State["ExceptionType"].Should().Be(nameof(SshConnectionException));

        attempts = 0;
        await FluentActions.Invoking(() => pipelines.Download.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new SftpPermissionDeniedException("denied");
        }).AsTask()).Should().ThrowAsync<SftpPermissionDeniedException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public void Retry_pipelines_are_empty_when_disabled()
    {
        var pipelines = new SftpRetryPipelines(TestData.Options(), TimeProvider.System, new CapturingLogger<SftpRetryPipelines>());

        pipelines.Open.Should().BeSameAs(ResiliencePipeline.Empty);
        pipelines.List.Should().BeSameAs(ResiliencePipeline.Empty);
        pipelines.Download.Should().BeSameAs(ResiliencePipeline.Empty);
        FluentActions.Invoking(() => new SftpRetryPipelines(null!, TimeProvider.System, new CapturingLogger<SftpRetryPipelines>())).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(TransientCases))]
    public void Transient_classification(Exception exception, bool transient)
    {
        SftpRetryPipelines.IsTransient(exception).Should().Be(transient);
    }

    public static TheoryData<Exception, bool> TransientCases() => new()
    {
        { new SshConnectionException("x"), true },
        { new SshOperationTimeoutException("x"), true },
        { new SocketException(), true },
        { new SftpPathNotFoundException("x"), false },
        { new SshAuthenticationException("x"), false },
        { new InvalidOperationException("x"), false },
    };

    [Fact]
    public async Task Retrying_session_retries_list_and_download_but_not_delete()
    {
        var pipelines = new SftpRetryPipelines(TestData.Options(options => { options.SftpRetryCount = 1; options.SftpRetryBaseDelayMilliseconds = 1; }), TimeProvider.System, new CapturingLogger<SftpRetryPipelines>());
        var inner = new Mock<ISftpSession>();
        var file = TestData.File("a.x12");
        var content = new FakeFileContent([1]);
        inner.SetupSequence(session => session.ListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SshConnectionException("x"))
            .ReturnsAsync([file]);
        inner.SetupSequence(session => session.DownloadAsync(file, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocketException())
            .ReturnsAsync(content);
        inner.Setup(session => session.DeleteAsync(file, It.IsAny<CancellationToken>())).ThrowsAsync(new SshConnectionException("x"));
        await using var session = new RetryingSftpSession(inner.Object, pipelines);

        (await session.ListAsync(CancellationToken.None)).Should().ContainSingle();
        (await session.DownloadAsync(file, CancellationToken.None)).Should().BeSameAs(content);
        await FluentActions.Invoking(() => session.DeleteAsync(file, CancellationToken.None)).Should().ThrowAsync<SshConnectionException>();

        inner.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        inner.Verify(s => s.DownloadAsync(file, It.IsAny<CancellationToken>()), Times.Exactly(2));
        inner.Verify(s => s.DeleteAsync(file, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retrying_session_never_retries_a_streaming_copy()
    {
        var pipelines = new SftpRetryPipelines(TestData.Options(options => { options.SftpRetryCount = 2; options.SftpRetryBaseDelayMilliseconds = 1; }), TimeProvider.System, new CapturingLogger<SftpRetryPipelines>());
        var inner = new Mock<ISftpSession>();
        var file = TestData.File("a.x12");
        using var destination = new MemoryStream();
        inner.Setup(session => session.CopyToAsync(file, destination, It.IsAny<CancellationToken>())).ThrowsAsync(new SocketException());
        await using var session = new RetryingSftpSession(inner.Object, pipelines);

        await FluentActions.Invoking(() => session.CopyToAsync(file, destination, CancellationToken.None)).Should().ThrowAsync<SocketException>();

        inner.Verify(s => s.CopyToAsync(file, destination, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retrying_factory_retries_open_and_wraps_the_session()
    {
        var pipelines = new SftpRetryPipelines(TestData.Options(options => { options.SftpRetryCount = 1; options.SftpRetryBaseDelayMilliseconds = 1; }), TimeProvider.System, new CapturingLogger<SftpRetryPipelines>());
        var inner = new Mock<ISftpSessionFactory>();
        var innerSession = new Mock<ISftpSession>();
        inner.SetupSequence(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SshOperationTimeoutException("x"))
            .ReturnsAsync(innerSession.Object);

        var session = await new RetryingSftpSessionFactory(inner.Object, pipelines).OpenAsync(TestData.Availity, CancellationToken.None);
        await session.DisposeAsync();

        session.Should().BeOfType<RetryingSftpSession>();
        inner.Verify(factory => factory.OpenAsync(TestData.Availity, It.IsAny<CancellationToken>()), Times.Exactly(2));
        innerSession.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Session_factory_resolves_credentials_and_warns_when_host_key_is_not_pinned()
    {
        var credentialProvider = new Mock<ISftpCredentialProvider>();
        credentialProvider.Setup(provider => provider.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Credentials);
        var logger = new CapturingLogger<SshNetSftpSessionFactory>();
        var factory = new SshNetSftpSessionFactory(credentialProvider.Object, _clientFactory.Object, new FakeTimeProvider(), TestData.Options(), logger);

        await using var session = await factory.OpenAsync(TestData.Availity, CancellationToken.None);

        session.Should().BeOfType<SshNetSftpSession>();
        logger.Sink.SingleEvent(2005).Level.Should().Be(LogLevel.Warning);
        var opening = logger.Sink.SingleEvent(2000);
        opening.State["SftpHost"].Should().Be("sftp.example.test");
        opening.State["PoolSize"].Should().Be(4);
        logger.Sink.SingleEvent(2001).State["DownloadDirectory"].Should().Be("/ReceiveFiles/");
        logger.Sink.Entries.Should().NotContain(entry => entry.Message.Contains("secret") || entry.Message.Contains("user"));
        await FluentActions.Invoking(() => factory.OpenAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Session_factory_does_not_warn_when_host_key_is_pinned()
    {
        var credentialProvider = new Mock<ISftpCredentialProvider>();
        credentialProvider
            .Setup(provider => provider.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SftpCredentials("h", 22, "u", "p", "/in", "SHA256:x"));
        var logger = new CapturingLogger<SshNetSftpSessionFactory>();
        var factory = new SshNetSftpSessionFactory(credentialProvider.Object, _clientFactory.Object, TimeProvider.System, TestData.Options(), logger);

        await using var session = await factory.OpenAsync(TestData.Availity, CancellationToken.None);

        logger.Sink.ByEventId(2005).Should().BeEmpty();
        FluentActions.Invoking(() => new SshNetSftpSessionFactory(credentialProvider.Object, _clientFactory.Object, TimeProvider.System, null!, logger)).Should().Throw<ArgumentNullException>();
    }

    private Task<SshNetSftpSession> Open(int poolSize = 4)
        => SshNetSftpSession.OpenAsync(_clientFactory.Object, Credentials, poolSize, TimeSpan.FromSeconds(30), _logger, CancellationToken.None);

    private static Mock<ISftpClient> NewClient()
    {
        var client = new Mock<ISftpClient>();
        var connected = false;
        client.Setup(c => c.IsConnected).Returns(() => connected);
        client.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>())).Callback(() => connected = true).Returns(Task.CompletedTask);
        return client;
    }

    private static ISftpFile Entry(string name, long length, DateTime modified, bool isDirectory = false)
    {
        var file = new Mock<ISftpFile>();
        file.Setup(f => f.Name).Returns(name);
        file.Setup(f => f.FullName).Returns($"/ReceiveFiles/{name}");
        file.Setup(f => f.Length).Returns(length);
        file.Setup(f => f.LastWriteTimeUtc).Returns(modified);
        file.Setup(f => f.IsDirectory).Returns(isDirectory);
        return file.Object;
    }

    private static async IAsyncEnumerable<ISftpFile> Entries(params ISftpFile[] entries)
    {
        foreach (var entry in entries)
        {
            await Task.Yield();
            yield return entry;
        }
    }
}

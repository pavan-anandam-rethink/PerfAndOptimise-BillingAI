using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Billing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class BillingTests
{
    private static readonly Uri BillingBase = new("https://billing.example.test/api/");

    [Theory]
    [InlineData("AV_999_20261104_0001.x12")]
    [InlineData("AV_277CA_20261104_0002.x12")]
    [InlineData("AV_835_20261104_0003.x12")]
    [InlineData("AV_TA1_20261104_0004.x12")]
    public async Task Json_content_matches_the_legacy_serializer_for_every_sample(string sample)
    {
        var bytes = TestData.Sample(sample);

        await AssertWireFormat(1, sample, "Availity", bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(48 * 1024)]
    [InlineData((48 * 1024) + 1)]
    [InlineData((3 * 48 * 1024) + 7)]
    public async Task Json_content_matches_the_legacy_serializer_across_block_boundaries(int size)
    {
        var bytes = Enumerable.Range(0, size).Select(index => (byte)((index * 31) + 7)).ToArray();

        await AssertWireFormat(8, "edge.x12", "Stedi", bytes);
    }

    [Fact]
    public async Task Json_content_escapes_names_like_the_legacy_serializer()
    {
        await AssertWireFormat(1, "a+b <c>&'d\" é\\x.x12", "Avail\"ity", TestData.Bytes("ISA*00~"));
    }

    [Fact]
    public void Json_content_reports_exact_length_and_media_type()
    {
        var bytes = TestData.Bytes("ISA*00*          *00~");
        using var content = new InboundFileJsonContent(1, "f.x12", "Availity", bytes);
        var expected = JsonSerializer.SerializeToUtf8Bytes(Request(1, "f.x12", "Availity", bytes));

        content.BodyLength.Should().Be(expected.Length);
        content.Headers.ContentLength.Should().Be(expected.Length);
        content.Headers.ContentType!.MediaType.Should().Be("application/json");
        content.Headers.ContentType.CharSet.Should().Be("utf-8");
    }

    [Fact]
    public async Task Json_content_honours_cancellation()
    {
        using var content = new InboundFileJsonContent(1, "f.x12", "Availity", new byte[100]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var target = new MemoryStream();

        var act = () => content.CopyToAsync(target, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("true", true, true, null)]
    [InlineData(" TRUE ", true, true, null)]
    [InlineData("\"true\"", true, true, null)]
    [InlineData("{\"success\":true,\"queued\":true,\"fileId\":42}", true, true, 42)]
    [InlineData("{\"Success\":true,\"Queued\":false,\"FileId\":5}", true, false, 5)]
    [InlineData("{\"fileId\":7}", true, true, 7)]
    [InlineData("{\"queued\":true}", true, true, null)]
    [InlineData("\"{\\\"success\\\":true}\"", true, true, null)]
    public void Parser_accepts_the_legacy_success_shapes(string body, bool accepted, bool queued, int? fileId)
    {
        var registration = BillingRegistrationParser.Parse(isSuccessStatusCode: true, body);

        registration.Accepted.Should().Be(accepted);
        registration.Queued.Should().Be(queued);
        registration.FileId.Should().Be(fileId);
        registration.Failure.Should().Be(queued ? null : InboundFailureMessages.BillingDidNotQueue);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"false\"")]
    [InlineData("{}")]
    [InlineData("{\"success\":false,\"fileId\":3}")]
    [InlineData("{\"queued\":false}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void Parser_rejects_other_success_bodies_with_the_legacy_message(string? body)
    {
        var registration = BillingRegistrationParser.Parse(isSuccessStatusCode: true, body);

        registration.Should().Be(BillingRegistration.Rejected(null));
        registration.Failure.Should().Be(InboundFailureMessages.BillingRejected);
    }

    [Theory]
    [InlineData("duplicate file", "duplicate file")]
    [InlineData("", "Billing rejected the file.")]
    [InlineData(null, "Billing rejected the file.")]
    public void Parser_turns_error_status_into_rejection_with_body(string? body, string expectedFailure)
    {
        var registration = BillingRegistrationParser.Parse(isSuccessStatusCode: false, body);

        registration.Accepted.Should().BeFalse();
        registration.Queued.Should().BeFalse();
        registration.Failure.Should().Be(expectedFailure);
    }

    [Fact]
    public async Task Registrar_posts_the_legacy_body_and_both_key_headers()
    {
        var content = TestData.Sample("AV_835_20261104_0003.x12");
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"success\":true,\"queued\":true,\"fileId\":11}");
        var logger = new CapturingLogger<BillingApiFileRegistrar>();
        var time = new FakeTimeProvider();
        handler.OnSend = () => time.Advance(TimeSpan.FromMilliseconds(40));
        var registrar = new BillingApiFileRegistrar(Factory(handler), Settings(new BillingApiSettings(BillingBase, "app-key", "x-application-key")), time, logger);

        var registration = await registrar.RegisterAsync(TestData.Availity, "AV_835_20261104_0003.x12", content, CancellationToken.None);

        registration.Should().Be(BillingRegistration.Succeeded(11));
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri.Should().Be(new Uri("https://billing.example.test/api/ClearingHouse/UploadEDIResponseFile"));
        handler.Headers["x-application-key"].Should().Equal("app-key");
        handler.Headers["XApiKey"].Should().Equal("app-key");
        handler.Body.Should().Equal(JsonSerializer.SerializeToUtf8Bytes(Request(1, "AV_835_20261104_0003.x12", "Availity", content)));
        var log = logger.Sink.SingleEvent(2010);
        log.State["StatusCode"].Should().Be(200);
        log.State["FileName"].Should().Be("AV_835_20261104_0003.x12");
        log.State["RequestBytes"].Should().Be((long)handler.Body.Length);
        log.State["ElapsedMilliseconds"].Should().Be(40L);
        log.Message.Should().NotContain("app-key");
    }

    [Fact]
    public async Task Registrar_sends_one_header_when_the_configured_header_is_the_legacy_one()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "true");
        var registrar = new BillingApiFileRegistrar(Factory(handler), Settings(new BillingApiSettings(BillingBase, "k", "XApiKey")), TimeProvider.System, new CapturingLogger<BillingApiFileRegistrar>());

        await registrar.RegisterAsync(TestData.Availity, "f.x12", new byte[1], CancellationToken.None);

        handler.Headers.Keys.Should().Contain("XApiKey").And.NotContain("x-application-key");
        handler.Headers["XApiKey"].Should().Equal("k");
    }

    [Fact]
    public async Task Registrar_sends_no_key_headers_without_a_key_and_returns_rejections()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "duplicate");
        var logger = new CapturingLogger<BillingApiFileRegistrar>();
        var registrar = new BillingApiFileRegistrar(Factory(handler), Settings(new BillingApiSettings(BillingBase, null, "x-application-key")), TimeProvider.System, logger);

        var registration = await registrar.RegisterAsync(TestData.Stedi, "f.x12", new byte[1], CancellationToken.None);

        registration.Should().Be(BillingRegistration.Rejected("duplicate"));
        handler.Headers.Keys.Should().NotContain("XApiKey").And.NotContain("x-application-key");
        logger.Sink.SingleEvent(2010).State["StatusCode"].Should().Be(400);
        await FluentActions.Invoking(() => registrar.RegisterAsync(null!, "f", new byte[1], CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Settings_provider_resolves_secret_names_and_literals()
    {
        var resolver = Resolver(new Dictionary<string, string>
        {
            ["Prd--Billing--Url"] = "https://billing.example.test/",
            ["Prd--Billing--Key"] = "secret-key",
        });
        var provider = new BillingApiSettingsProvider(Options(o => { o.BaseUrl = "Prd--Billing--Url"; o.ApiKey = "Prd--Billing--Key"; o.HeaderName = "X-Custom"; }), resolver.Object);

        var settings = await provider.GetAsync(CancellationToken.None);

        settings.BaseAddress.Should().Be(new Uri("https://billing.example.test/"));
        settings.ApiKey.Should().Be("secret-key");
        settings.HeaderName.Should().Be("X-Custom");
        settings.ToString().Should().Be("https://billing.example.test/").And.NotContain("secret-key");
        resolver.Verify(r => r.ResolveAsync("Prd--Billing--Url", "BillingApiUrl", SecretNamePatternPolicy.Instance, It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(r => r.ResolveAsync("Prd--Billing--Key", "BillingApiKey", SecretNamePatternPolicy.Instance, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settings_provider_uses_legacy_fallbacks_when_primary_values_are_blank()
    {
        var resolver = new Mock<ISecretResolver>(MockBehavior.Strict);
        var provider = new BillingApiSettingsProvider(Options(o => { o.FallbackBaseUrl = "http://localhost:5001/"; o.FallbackApiKey = "fallback-key"; }), resolver.Object);

        var settings = await provider.GetAsync(CancellationToken.None);

        settings.BaseAddress.Should().Be(new Uri("http://localhost:5001/"));
        settings.ApiKey.Should().Be("fallback-key");
        settings.HeaderName.Should().Be(BillingApiOptions.DefaultHeaderName);
    }

    [Fact]
    public async Task Settings_provider_returns_null_key_when_no_key_is_configured()
    {
        var provider = new BillingApiSettingsProvider(Options(o => { o.FallbackBaseUrl = "http://localhost:5001/"; o.FallbackApiKey = " "; }), Mock.Of<ISecretResolver>());

        (await provider.GetAsync(CancellationToken.None)).ApiKey.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a uri")]
    public async Task Settings_provider_fails_when_no_url_is_configured(string? fallback)
    {
        var provider = new BillingApiSettingsProvider(Options(o => o.FallbackBaseUrl = fallback), Mock.Of<ISecretResolver>());

        await provider.Invoking(p => p.GetAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>().WithMessage("BillingApiUrl is not configured.");
    }

    [Fact]
    public async Task Settings_provider_fails_when_the_url_is_not_absolute()
    {
        var resolver = Resolver(new Dictionary<string, string> { ["billing-url"] = "relative/path" });
        var provider = new BillingApiSettingsProvider(Options(o => o.BaseUrl = "billing-url"), resolver.Object);

        await provider.Invoking(p => p.GetAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>().WithMessage("BillingApiUrl must resolve to an absolute URI.");
        FluentActions.Invoking(() => new BillingApiSettingsProvider(null!, resolver.Object)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_read_the_legacy_keys()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BillingApiUrl"] = "Prd--Clearinghouse--BillingApiUrl",
            ["BillingApiKey"] = "Prd--Clearinghouse--BillingApiKey",
            ["BillingApiClient:ApiBaseUrl"] = "http://localhost:5001",
            ["BillingApiClient:ApiKey"] = "dev-key",
            ["HeaderKey"] = "x-custom-key",
        }).Build();
        var options = new BillingApiOptions();

        options.Configure(configuration);

        options.BaseUrl.Should().Be("Prd--Clearinghouse--BillingApiUrl");
        options.ApiKey.Should().Be("Prd--Clearinghouse--BillingApiKey");
        options.FallbackBaseUrl.Should().Be("http://localhost:5001");
        options.FallbackApiKey.Should().Be("dev-key");
        options.HeaderName.Should().Be("x-custom-key");
    }

    [Fact]
    public void Options_default_the_header_and_reject_null()
    {
        var options = new BillingApiOptions();

        options.Configure(new ConfigurationBuilder().Build());

        options.HeaderName.Should().Be("x-application-key");
        options.BaseUrl.Should().BeNull();
        FluentActions.Invoking(() => options.Configure(null!)).Should().Throw<ArgumentNullException>();
        BillingApiPaths.UploadEdiResponseFile.Should().Be("ClearingHouse/UploadEDIResponseFile");
        BillingApiPaths.LegacyApiKeyHeader.Should().Be("XApiKey");
    }

    private static async Task AssertWireFormat(int clearingHouseId, string fileName, string title, byte[] bytes)
    {
        using var content = new InboundFileJsonContent(clearingHouseId, fileName, title, bytes);
        var expected = JsonSerializer.SerializeToUtf8Bytes(Request(clearingHouseId, fileName, title, bytes));

        var actual = await content.ReadAsByteArrayAsync();

        actual.Should().Equal(expected);
        content.BodyLength.Should().Be(expected.Length);
        var roundTrip = JsonSerializer.Deserialize<DownloadSftpFileRequest>(actual)!;
        roundTrip.ClearingHouseId.Should().Be(clearingHouseId);
        roundTrip.Data.Should().Equal(bytes);
        roundTrip.FileName.Should().Be(fileName);
        roundTrip.Title.Should().Be(title);
    }

    private static DownloadSftpFileRequest Request(int clearingHouseId, string fileName, string title, byte[] bytes)
        => new() { ClearingHouseId = clearingHouseId, Data = bytes, FileName = fileName, Title = title };

    private static Microsoft.Extensions.Options.IOptions<BillingApiOptions> Options(Action<BillingApiOptions> configure)
    {
        var options = new BillingApiOptions();
        configure(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static Mock<ISecretResolver> Resolver(Dictionary<string, string> secrets)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? value, string _, ISecretReferencePolicy _, CancellationToken _) => secrets[value!]);
        return resolver;
    }

    private static IBillingApiSettingsProvider Settings(BillingApiSettings settings)
        => Mock.Of<IBillingApiSettingsProvider>(provider => provider.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
        => Mock.Of<IHttpClientFactory>(factory => factory.CreateClient(BillingApiPaths.HttpClientName) == new HttpClient(handler, false));

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public Action? OnSend { get; set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public Dictionary<string, string[]> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = header.Value.ToArray();
            }

            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            OnSend?.Invoke();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}

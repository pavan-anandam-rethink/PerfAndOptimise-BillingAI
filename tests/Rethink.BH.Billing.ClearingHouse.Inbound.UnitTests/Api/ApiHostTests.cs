using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Api;

/// <summary>Hosts the real API with in-memory SFTP, backup, and Billing fakes to verify the HTTP contract end to end.</summary>
public sealed class ApiHostTests : IClassFixture<ApiHostTests.InboundApiFactory>
{
    private const string Route = "/ClearingHouseClaimProcessor/downloadEdiDataFromSftp";
    private const string ValidKey = "11111111-1111-1111-1111-111111111111";

    private readonly InboundApiFactory _factory;

    public ApiHostTests(InboundApiFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Unknown_clearing_house_returns_400_text()
    {
        using var client = Client("x-application-key");

        var response = await client.PostAsJsonAsync(Route, 99);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("Invalid clearing house ID.");
    }

    [Fact]
    public async Task No_files_returns_204()
    {
        using var client = Client("x-application-key");

        var response = await client.PostAsJsonAsync(Route, 1);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task All_files_processed_returns_200_and_files_are_backed_up_registered_and_deleted()
    {
        _factory.Session.With(TestData.File("AV_999_20261104_0001.x12"), TestData.Sample("AV_999_20261104_0001.x12"));
        _factory.Session.With(TestData.File("AV_835_20261104_0003.x12", 1), TestData.Sample("AV_835_20261104_0003.x12"));
        using var client = Client("XApiKey");

        var response = await client.PostAsJsonAsync(Route, 1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        _factory.Backup.Stored.Keys.Should().BeEquivalentTo("AV_999_20261104_0001.x12", "AV_835_20261104_0003.x12");
        _factory.Billing.Registered.Values.Should().AllSatisfy(registration => registration.Title.Should().Be("Availity"));
        _factory.Session.Deleted.Should().BeEquivalentTo("AV_999_20261104_0001.x12", "AV_835_20261104_0003.x12");
    }

    [Fact]
    public async Task Files_left_on_sftp_return_500_with_legacy_camel_case_counts()
    {
        _factory.Session.With(TestData.File("ok.x12"));
        _factory.Session.With(TestData.File("rejected.x12", 1));
        _factory.Billing.Responses["rejected.x12"] = BillingRegistration.Rejected("duplicate");
        using var client = Client("x-application-key");

        var response = await client.PostAsJsonAsync(Route, 1);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.ReplaceLineEndings("\n").Should().Be("{\n  \"failed\": 1,\n  \"total\": 2\n}");
        body.Should().NotContain("rejected.x12").And.NotContain("duplicate");
        _factory.Session.Deleted.Should().ContainSingle().Which.Should().Be("ok.x12");
    }

    [Fact]
    public async Task Missing_or_invalid_application_key_is_rejected()
    {
        using var anonymous = _factory.CreateClient();
        using var invalid = _factory.CreateClient();
        invalid.DefaultRequestHeaders.Add("x-application-key", "not-a-valid-key");

        (await anonymous.PostAsJsonAsync(Route, 1)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await invalid.PostAsJsonAsync(Route, 1)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.SessionFactory.Verify(f => f.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Health_check_and_swagger_are_served()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync(new Uri("/healthcheck/live", UriKind.Relative))).IsSuccessStatusCode.Should().BeTrue();
        var swagger = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
        swagger.Should().Contain("/ClearingHouseClaimProcessor/downloadEdiDataFromSftp").And.Contain("x-application-key");
    }

    private HttpClient Client(string headerName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(headerName, ValidKey);
        return client;
    }

    public sealed class InboundApiFactory : WebApplicationFactory<Program>
    {
        public FakeSftpSession Session { get; private set; } = new();

        public FakeBackupStore Backup { get; private set; } = new();

        public FakeBillingRegistrar Billing { get; private set; } = new();

        public Mock<ISftpSessionFactory> SessionFactory { get; } = new();

        public void Reset()
        {
            Session = new FakeSftpSession();
            Backup = new FakeBackupStore();
            Billing = new FakeBillingRegistrar();
            SessionFactory.Reset();
            SessionFactory
                .Setup(f => f.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Session);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = InfrastructureRegistrationTests.ValidSettings();
                values["KeyVaultUri"] = string.Empty;
                values["ApplicationSettings:ApplicationKeySettings:AcceptedApplicationKeys:0"] = ValidKey;
                values["ApplicationSettings:ApplicationKeySettings:CheckAcceptedApplicationKeys"] = "true";
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISftpSessionFactory>();
                services.RemoveAll<IBackupFileStore>();
                services.RemoveAll<IBillingFileRegistrar>();
                services.AddSingleton(_ => SessionFactory.Object);
                services.AddSingleton<IBackupFileStore>(_ => new BackupProxy(this));
                services.AddSingleton<IBillingFileRegistrar>(_ => new BillingProxy(this));
            });
        }

        private sealed class BackupProxy(InboundApiFactory factory) : IBackupFileStore
        {
            public Task<BackupWriteResult> WriteOnceAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
                => factory.Backup.WriteOnceAsync(fileName, content, cancellationToken);
        }

        private sealed class BillingProxy(InboundApiFactory factory) : IBillingFileRegistrar
        {
            public Task<BillingRegistration> RegisterAsync(ClearingHouseDescriptor clearingHouse, string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
                => factory.Billing.RegisterAsync(clearingHouse, fileName, content, cancellationToken);
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class InfrastructureRegistrationTests
{
    public static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Clearinghouses:Availity:Id"] = "1",
        ["Clearinghouses:Availity:Title"] = "Availity",
        ["Clearinghouses:Availity:Hosts"] = "availity-hosts",
        ["Clearinghouses:Availity:Port"] = "availity-port",
        ["Clearinghouses:Availity:UserName"] = "availity-username",
        ["Clearinghouses:Availity:UserPassword"] = "availity-password",
        ["Clearinghouses:Availity:DownloadDirectory"] = "availity-downloadDirectory",
        ["AvailityBackup"] = "availitybackup",
        ["ConnectionStrings:BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
        ["BillingApiUrl"] = "http://localhost:5001/",
        ["BillingApiKey"] = "dev-key",
        ["KeyVault:SecretCacheMinutes"] = "5",
    };

    [Fact]
    public void Registers_every_adapter_as_a_singleton()
    {
        using var provider = Build(ValidSettings());

        provider.GetRequiredService<ISecretResolver>().Should().BeOfType<KeyVaultSecretResolver>();
        provider.GetRequiredService<KeyVaultClientAccessor>().Client.Should().BeNull();
        provider.GetRequiredService<IOptions<KeyVaultOptions>>().Value.SecretCacheMinutes.Should().Be(5);
        var catalog = provider.GetRequiredService<IClearingHouseCatalog>();
        catalog.Should().BeSameAs(provider.GetRequiredService<IClearingHouseSettingsSource>());
        catalog.FindById(1)!.Title.Should().Be("Availity");
        provider.GetRequiredService<ISftpCredentialProvider>().Should().BeOfType<ClearingHouseSftpCredentialProvider>();
        provider.GetRequiredService<ISftpClientFactory>().Should().BeOfType<SshNetSftpClientFactory>();
        provider.GetRequiredService<ISftpSessionFactory>().Should().BeOfType<RetryingSftpSessionFactory>();
        provider.GetRequiredService<IBackupContainerProvider>().Should().BeOfType<BackupContainerProvider>();
        provider.GetRequiredService<IBackupFileStore>().Should().BeOfType<AzureBlobBackupFileStore>();
        provider.GetRequiredService<IBillingApiSettingsProvider>().Should().BeOfType<BillingApiSettingsProvider>();
        provider.GetRequiredService<IBillingFileRegistrar>().Should().BeOfType<BillingApiFileRegistrar>();
        provider.GetRequiredService<IBillingFileRegistrar>().Should().BeSameAs(provider.GetRequiredService<IBillingFileRegistrar>());
        provider.GetRequiredService<IOptions<BillingApiOptions>>().Value.ApiKey.Should().Be("dev-key");
        provider.GetRequiredService<IOptions<BackupStorageOptions>>().Value.ContainerName.Should().Be("availitybackup");
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(BillingApiPaths.HttpClientName).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Clearinghouses:Availity:Id", "Clearinghouses is not configured.")]
    [InlineData("Clearinghouses:Availity:Hosts", "Each clearing house needs Id, Title, Hosts, Port, UserName, UserPassword, and DownloadDirectory.")]
    [InlineData("AvailityBackup", "AvailityBackup is not configured.")]
    [InlineData("ConnectionStrings:BlobStorage:ConnectionString", "ConnectionStrings:BlobStorage:ConnectionString is not configured.")]
    public void Missing_settings_fail_with_the_legacy_messages(string removedKey, string expectedMessage)
    {
        var settings = ValidSettings();
        settings.Remove(removedKey);
        using var provider = Build(settings);

        var act = () =>
        {
            _ = provider.GetRequiredService<IOptions<ClearingHouseCatalogOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<BackupStorageOptions>>().Value;
        };

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void Requires_configuration()
    {
        FluentActions.Invoking(() => new ServiceCollection().AddInboundInfrastructure(null!)).Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddInboundApplication(configuration)
            .AddInboundInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

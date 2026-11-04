using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;

/// <summary>Registers the SFTP, Key Vault, Blob backup, and Billing adapters.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds every inbound adapter as a singleton (all are stateless or thread-safe) and validates configuration on start
    /// with the same rules and messages as the legacy service.
    /// </summary>
    public static IServiceCollection AddInboundInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        AddSecrets(services, configuration);
        AddClearingHouses(services);
        AddSftp(services);
        AddBackup(services);
        AddBilling(services);
        return services;
    }

    private static void AddSecrets(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeyVaultOptions>()
            .Bind(configuration.GetSection(KeyVaultOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(_ => KeyVaultClientAccessor.FromConfiguration(configuration));
        services.AddSingleton<ISecretResolver, KeyVaultSecretResolver>();
    }

    private static void AddClearingHouses(IServiceCollection services)
    {
        services.AddOptions<ClearingHouseCatalogOptions>()
            .Configure<IConfiguration>((options, config) => options.ClearingHouses = ClearingHouseCatalogOptions.Bind(config))
            .Validate(options => options.ClearingHouses.Count > 0, "Clearinghouses is not configured.")
            .Validate(options => options.IsValid, "Each clearing house needs Id, Title, Hosts, Port, UserName, UserPassword, and DownloadDirectory.")
            .ValidateOnStart();
        services.AddSingleton<ConfiguredClearingHouseCatalog>();
        services.AddSingleton<IClearingHouseCatalog>(provider => provider.GetRequiredService<ConfiguredClearingHouseCatalog>());
        services.AddSingleton<IClearingHouseSettingsSource>(provider => provider.GetRequiredService<ConfiguredClearingHouseCatalog>());
    }

    private static void AddSftp(IServiceCollection services)
    {
        services.AddSingleton<ISftpCredentialProvider, ClearingHouseSftpCredentialProvider>();
        services.AddSingleton<ISftpClientFactory, SshNetSftpClientFactory>();
        services.AddSingleton<SftpRetryPipelines>();
        services.AddSingleton<SshNetSftpSessionFactory>();
        services.AddSingleton<ISftpSessionFactory>(provider => new RetryingSftpSessionFactory(
            provider.GetRequiredService<SshNetSftpSessionFactory>(),
            provider.GetRequiredService<SftpRetryPipelines>()));
    }

    private static void AddBackup(IServiceCollection services)
    {
        services.AddOptions<BackupStorageOptions>()
            .Configure<IConfiguration>((options, config) => options.Configure(config))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ContainerName), "AvailityBackup is not configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "ConnectionStrings:BlobStorage:ConnectionString is not configured.")
            .ValidateOnStart();
        services.AddSingleton<IBackupContainerProvider, BackupContainerProvider>();
        services.AddSingleton<IBackupFileStore, AzureBlobBackupFileStore>();
    }

    private static void AddBilling(IServiceCollection services)
    {
        services.AddOptions<BillingApiOptions>()
            .Configure<IConfiguration>((options, config) => options.Configure(config));
        services.AddHttpClient(BillingApiPaths.HttpClientName);
        services.AddSingleton<IBillingApiSettingsProvider, BillingApiSettingsProvider>();
        services.AddSingleton<IBillingFileRegistrar, BillingApiFileRegistrar>();
    }
}

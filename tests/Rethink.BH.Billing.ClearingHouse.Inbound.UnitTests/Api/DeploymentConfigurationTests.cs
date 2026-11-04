using Microsoft.Extensions.Configuration;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Api;

/// <summary>
/// Guards the Helm values that replace the removed appsettings.QA/Staging/Production.json: every key must override a setting
/// the service reads, and the resolved values must match the legacy files.
/// </summary>
public sealed class DeploymentConfigurationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("qa", "QA", "https://bai-ch-eus2-qas-kv.vault.azure.net/", "QA--Clearinghouse--ConnectionStrings--BlobStorage--ConnectionString")]
    [InlineData("staging", "Stg", "https://bai-ch-eus2-prd-kv.vault.azure.net/", "Stg--Clearinghouse--ConnectionStrings--BlobStorage--ConnectionString")]
    [InlineData("production", "Prd", "https://rta-billing-prd-kv.vault.azure.net/", "Prd--Clearinghouse--ConnectionStrings--BlobStoragge--ConnectionString")]
    public void Environment_values_override_the_legacy_settings(string environment, string prefix, string keyVaultUri, string blobSecret)
    {
        var overrides = ReadInboundConfig(environment);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot, "src", "Rethink.BH.Billing.ClearingHouse.Inbound.Api", "appsettings.json"))
            .AddInMemoryCollection(overrides.ToDictionary(pair => pair.Key.Replace("__", ":", StringComparison.Ordinal), pair => (string?)pair.Value))
            .Build();
        var billing = new BillingApiOptions();
        billing.Configure(configuration);
        var backup = new BackupStorageOptions();
        backup.Configure(configuration);

        configuration["KeyVaultUri"].Should().Be(keyVaultUri);
        configuration["ApplicationInsights:InstrumentationKey"].Should().Be($"{prefix}--Clearinghouse--ApplicationInsights--InstrumentationKey");
        configuration["ApplicationInsights:APPLICATIONINSIGHTS_CONNECTION_STRING"].Should().Be($"{prefix}--Billing--ApplicationInsights--APPLICATIONINSIGHTS--CONNECTIONSTRING");
        billing.BaseUrl.Should().Be($"{prefix}--Clearinghouse--BillingApiUrl");
        billing.ApiKey.Should().Be($"{prefix}--Clearinghouse--BillingApiKey");
        billing.HeaderName.Should().Be("x-application-key");
        backup.ConnectionString.Should().Be(blobSecret);
        backup.ContainerName.Should().Be("billing-availity-backup");
        HostSettings.ShowSwagger(configuration).Should().BeTrue();
        configuration["ApplicationSettings:ApplicationKeySettings:CheckAcceptedApplicationKeys"].Should().Be("False");
        configuration["InboundDownload:MaxRunMinutes"].Should().Be("13");
        configuration["Clearinghouses:Availity:Hosts"].Should().Be("availity-hosts");
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("staging")]
    [InlineData("production")]
    public void Environment_values_only_use_known_keys(string environment)
    {
        string[] known =
        [
            "KeyVaultUri",
            "ApplicationInsights__InstrumentationKey",
            "ApplicationInsights__APPLICATIONINSIGHTS_CONNECTION_STRING",
            "BillingApiUrl",
            "BillingApiKey",
            "ConnectionStrings__BlobStorage__ConnectionString",
            "Swagger__Show",
        ];

        ReadInboundConfig(environment).Keys.Should().OnlyContain(key => known.Contains(key));
    }

    [Fact]
    public void Only_base_and_development_appsettings_ship_with_the_service()
    {
        var files = Directory.GetFiles(Path.Combine(RepositoryRoot, "src", "Rethink.BH.Billing.ClearingHouse.Inbound.Api"), "appsettings*.json")
            .Select(Path.GetFileName);

        files.Should().BeEquivalentTo("appsettings.json", "appsettings.Development.json");
    }

    private static Dictionary<string, string> ReadInboundConfig(string environment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in File.ReadAllLines(Path.Combine(RepositoryRoot, "deployment", "helm", $"values-{environment}.yaml")))
        {
            if (line.StartsWith("inboundConfig:", StringComparison.Ordinal))
            {
                inSection = true;
                continue;
            }

            if (!inSection || line.TrimStart().StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
        }

        return values;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rethink.BH.Billing.ClearingHouse.Inbound.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

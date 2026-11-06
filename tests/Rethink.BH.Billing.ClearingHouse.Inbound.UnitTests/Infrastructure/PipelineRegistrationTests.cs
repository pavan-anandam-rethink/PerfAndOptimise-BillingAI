using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class PipelineRegistrationTests
{
    public static Dictionary<string, string?> ActiveSettings()
    {
        var settings = InfrastructureRegistrationTests.ValidSettings();
        settings["Pipeline:Mode"] = "Active";
        settings["Pipeline:ClearingHouseIds:0"] = "1";
        settings["ConnectionStrings:InboundManifest"] = "Server=tcp:sql.example.net;Database=inbound;Authentication=Active Directory Default;";
        settings["ServiceBus:FullyQualifiedNamespace"] = "sb-test.servicebus.windows.net";
        return settings;
    }

    [Fact]
    public async Task Pipeline_adapters_are_registered_even_when_the_pipeline_is_off()
    {
        await using var provider = Build(InfrastructureRegistrationTests.ValidSettings());

        provider.GetRequiredService<IInboundFileManifest>().Should().BeOfType<SqlInboundFileManifest>();
        provider.GetRequiredService<IManifestDbContextFactory>().Should().BeOfType<SqlManifestDbContextFactory>();
        provider.GetRequiredService<ILandingFileStore>().Should().BeOfType<AzureBlobLandingFileStore>();
        provider.GetRequiredService<IRunLeaseProvider>().Should().BeOfType<BlobRunLeaseProvider>();
        provider.GetRequiredService<IInboundWorkSender>().Should().BeSameAs(provider.GetRequiredService<ServiceBusInboundWorkSender>());
        provider.GetRequiredService<InboundWorkMessageHandler>().Should().NotBeNull();
        provider.GetRequiredService<IInboundWorkDispatcher>().Should().BeOfType<InboundWorkDispatcher>();
        provider.GetRequiredService<IOptions<PipelineOptions>>().Value.Mode.Should().Be(PipelineMode.Off);
        provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.IsConfigured.Should().BeFalse();
        provider.GetRequiredService<IOptions<ManifestStorageOptions>>().Value.ConnectionString.Should().BeEmpty();
        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public async Task Active_settings_bind_and_validate()
    {
        var settings = ActiveSettings();
        settings["Pipeline:Manifest:CommandTimeoutSeconds"] = "45";
        settings["Pipeline:Landing:ContainerName"] = "landing";
        await using var provider = Build(settings);

        provider.GetRequiredService<IOptions<ManifestStorageOptions>>().Value.CommandTimeoutSeconds.Should().Be(45);
        provider.GetRequiredService<IOptions<ManifestStorageOptions>>().Value.ConnectionString.Should().StartWith("Server=tcp:");
        provider.GetRequiredService<IOptions<LandingStorageOptions>>().Value.ContainerName.Should().Be("landing");
        provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.FullyQualifiedNamespace.Should().Be("sb-test.servicebus.windows.net");
        provider.GetRequiredService<IOptions<PipelineOptions>>().Value.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("ConnectionStrings:InboundManifest", "ConnectionStrings:InboundManifest is required when Pipeline:Mode is not Off.")]
    [InlineData("ServiceBus:FullyQualifiedNamespace", "ServiceBus:FullyQualifiedNamespace or ServiceBus:ConnectionString is required when Pipeline:Mode is not Off.")]
    [InlineData("Pipeline:ClearingHouseIds:0", "Pipeline:ClearingHouseIds must list at least one clearing house when the pipeline is on.")]
    public void Enabled_pipeline_requires_its_connections(string removedKey, string expectedMessage)
    {
        var settings = ActiveSettings();
        settings.Remove(removedKey);
        using var provider = Build(settings);

        var act = () =>
        {
            _ = provider.GetRequiredService<IOptions<ManifestStorageOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<PipelineOptions>>().Value;
        };

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void Retry_ceiling_must_not_be_below_the_base_delay()
    {
        var settings = ActiveSettings();
        settings["Pipeline:RetryBaseDelaySeconds"] = "60";
        settings["Pipeline:RetryMaxDelaySeconds"] = "30";
        using var provider = Build(settings);

        var act = () => provider.GetRequiredService<IOptions<PipelineOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("RetryMaxDelaySeconds");
    }

    [Theory]
    [InlineData(InboundWorkStage.Download, "inbound-download", 6)]
    [InlineData(InboundWorkStage.Process, "inbound-process", 2)]
    public async Task Worker_registration_reads_the_stage_queue_and_concurrency(InboundWorkStage stage, string queue, int concurrency)
    {
        var settings = ActiveSettings();
        settings["Pipeline:DownloadConcurrency"] = "6";
        settings["Pipeline:ProcessConcurrency"] = "2";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await using var provider = Services(configuration).AddInboundWorker(stage).BuildServiceProvider();

        var worker = provider.GetServices<IHostedService>().Should().ContainSingle().Subject;

        worker.Should().BeOfType<InboundWorkerHostedService>();
        var field = typeof(InboundWorkerHostedService).GetField("_settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.GetValue(worker).Should().Be(new InboundWorkerSettings(stage, queue, concurrency, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Requires_configuration()
    {
        FluentActions.Invoking(() => new ServiceCollection().AddPipelineInfrastructure(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Dispatcher_rejects_an_unknown_stage()
    {
        var dispatcher = new InboundWorkDispatcher(Mock.Of<MediatR.ISender>());

        var act = () => dispatcher.DispatchAsync((InboundWorkStage)42, new InboundFileWorkItem(Guid.NewGuid(), 1, 0), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
        => Services(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static IServiceCollection Services(IConfiguration configuration)
        => new ServiceCollection()
            .AddLogging()
            .AddSingleton(configuration)
            .AddInboundApplication(configuration)
            .AddInboundInfrastructure(configuration);
}

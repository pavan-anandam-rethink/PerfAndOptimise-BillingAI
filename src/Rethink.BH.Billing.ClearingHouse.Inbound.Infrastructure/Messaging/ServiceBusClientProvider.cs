using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>
/// The process-wide <see cref="ServiceBusClient"/> (one AMQP connection shared by every sender and processor). Created on
/// first use, so a pod that never touches the pipeline never connects. A failed resolution is not cached.
/// </summary>
public sealed class ServiceBusClientProvider : IAsyncDisposable
{
    private readonly ServiceBusOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly Func<ServiceBusOptions, string?, ServiceBusClient> _clientFactory;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private ServiceBusClient? _client;

    /// <summary>Creates the provider with the default client factory.</summary>
    public ServiceBusClientProvider(IOptions<ServiceBusOptions> options, ISecretResolver secretResolver)
        : this(options, secretResolver, CreateClient)
    {
    }

    /// <summary>Creates the provider with a custom client factory (receives the resolved connection string, or null).</summary>
    public ServiceBusClientProvider(IOptions<ServiceBusOptions> options, ISecretResolver secretResolver, Func<ServiceBusOptions, string?, ServiceBusClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _secretResolver = secretResolver;
        _clientFactory = clientFactory;
    }

    /// <summary>Returns the shared client.</summary>
    public async ValueTask<ServiceBusClient> GetClientAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _client);
        if (existing is not null)
        {
            return existing;
        }

        await _creationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var connectionString = string.IsNullOrWhiteSpace(_options.ConnectionString)
                ? null
                : await _secretResolver
                    .ResolveAsync(_options.ConnectionString, ServiceBusOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, cancellationToken)
                    .ConfigureAwait(false);

            var client = _clientFactory(_options, connectionString);
            Volatile.Write(ref _client, client);
            return client;
        }
        finally
        {
            _creationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _creationLock.Dispose();
    }

    private static ServiceBusClient CreateClient(ServiceBusOptions options, string? connectionString)
        => connectionString is null
            ? new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential())
            : new ServiceBusClient(connectionString);
}

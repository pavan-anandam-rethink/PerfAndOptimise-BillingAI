using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>
/// Posts a downloaded file to Billing <c>UploadEDIResponseFile</c>. Billing writes the claim EDI row and publishes the
/// response-processing message. The request is not retried because the endpoint is not idempotent.
/// </summary>
public sealed class BillingApiFileRegistrar : IBillingFileRegistrar
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBillingApiSettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BillingApiFileRegistrar> _logger;

    /// <summary>Creates the registrar.</summary>
    public BillingApiFileRegistrar(
        IHttpClientFactory httpClientFactory,
        IBillingApiSettingsProvider settingsProvider,
        TimeProvider timeProvider,
        ILogger<BillingApiFileRegistrar> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BillingRegistration> RegisterAsync(
        ClearingHouseDescriptor clearingHouse,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);

        var settings = await _settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        using var body = new InboundFileJsonContent(clearingHouse.Id, fileName, clearingHouse.Title, content);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseAddress, BillingApiPaths.UploadEdiResponseFile))
        {
            Content = body,
        };
        AddApiKeyHeaders(request, settings);

        var startedAt = _timeProvider.GetTimestamp();
        using var response = await _httpClientFactory
            .CreateClient(BillingApiPaths.HttpClientName)
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        InfrastructureLog.BillingResponseReceived(
            _logger,
            (int)response.StatusCode,
            fileName,
            body.BodyLength,
            (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);

        return BillingRegistrationParser.Parse(response.IsSuccessStatusCode, responseBody);
    }

    private static void AddApiKeyHeaders(HttpRequestMessage request, BillingApiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(settings.HeaderName, settings.ApiKey);
        if (!string.Equals(settings.HeaderName, BillingApiPaths.LegacyApiKeyHeader, StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(BillingApiPaths.LegacyApiKeyHeader, settings.ApiKey);
        }
    }
}

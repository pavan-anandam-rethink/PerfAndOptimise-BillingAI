using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Trims a reason and cuts it at <see cref="InboundDownloadOptions.FailureMessageMaxLength"/>.</summary>
public sealed class FailureMessageFormatter : IFailureMessageFormatter
{
    private readonly int _maxLength;

    /// <summary>Creates the formatter.</summary>
    public FailureMessageFormatter(IOptions<InboundDownloadOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxLength = options.Value.FailureMessageMaxLength;
    }

    /// <inheritdoc />
    public string Format(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return InboundFailureMessages.DownloadFailed;
        }

        var trimmed = reason.Trim();
        return trimmed.Length <= _maxLength ? trimmed : trimmed[.._maxLength];
    }
}

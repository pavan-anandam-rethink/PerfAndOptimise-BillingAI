using Microsoft.AspNetCore.Mvc;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;

/// <summary>Maps <see cref="NotSupportedException"/> and <see cref="NotImplementedException"/> to 501.</summary>
public sealed class NotImplementedProblemMapper : IProblemMapper
{
    /// <inheritdoc />
    public bool IsClientError => true;

    /// <inheritdoc />
    public bool CanMap(Exception exception) => exception is NotSupportedException or NotImplementedException;

    /// <inheritdoc />
    public ProblemResponse Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ProblemResponse(
            StatusCodes.Status501NotImplemented,
            new ProblemDetails
            {
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.2",
                Title = "Not implemented",
                Detail = exception.Message,
            });
    }
}

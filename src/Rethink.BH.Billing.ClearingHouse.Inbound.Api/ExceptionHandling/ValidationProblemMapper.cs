using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;

/// <summary>Maps <see cref="ValidationException"/> to 400 with the legacy validation body.</summary>
public sealed class ValidationProblemMapper : IProblemMapper
{
    /// <inheritdoc />
    public bool IsClientError => true;

    /// <inheritdoc />
    public bool CanMap(Exception exception) => exception is ValidationException;

    /// <inheritdoc />
    public ProblemResponse Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["Validation"] = [exception.Message] };
        return new ProblemResponse(
            StatusCodes.Status400BadRequest,
            new ValidationProblemDetails(errors) { Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1" });
    }
}

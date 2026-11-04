using Microsoft.AspNetCore.Mvc;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;

/// <summary>
/// Maps one family of exceptions to a status code and ProblemDetails. New mappings are added as new implementations
/// without changing <see cref="GlobalExceptionHandler"/>.
/// </summary>
public interface IProblemMapper
{
    /// <summary>True when this mapper handles <paramref name="exception"/>.</summary>
    bool CanMap(Exception exception);

    /// <summary>True when the exception is a client error that is logged as a warning rather than an error.</summary>
    bool IsClientError { get; }

    /// <summary>Builds the response.</summary>
    ProblemResponse Map(Exception exception);
}

/// <summary>Status code and body written for a mapped exception.</summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="Problem">Response body.</param>
public sealed record ProblemResponse(int StatusCode, ProblemDetails Problem);

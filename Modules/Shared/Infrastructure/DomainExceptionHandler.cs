using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Maps <see cref="DomainException"/> to its carried HTTP status code as an RFC 7807 response.
/// Registered before the catch-all <see cref="GlobalExceptionHandler"/> so business failures
/// surface as 4xx rather than a generic 500.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = domainException.StatusCode,
            Title = "Request error",
            Detail = domainException.Message,
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = domainException.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Maps <see cref="AuthException"/> to its carried HTTP status code as an RFC 7807 response.
/// Registered before the catch-all <c>GlobalExceptionHandler</c> so auth failures surface
/// as 401/404/409 rather than a generic 500.
/// </summary>
public sealed class AuthExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AuthException authException)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = authException.StatusCode,
            Title = "Authentication error",
            Detail = authException.Message,
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = authException.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

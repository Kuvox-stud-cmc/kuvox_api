using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Maps <see cref="NotImplementedException"/> — thrown by the not-yet-built "real" service
/// methods — to an honest RFC 7807 <c>501 Not Implemented</c> response, so the scaffolded
/// endpoints are clearly distinguishable from the working mockup endpoints in Scalar.
/// </summary>
public sealed class NotImplementedExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not NotImplementedException)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = "Not implemented",
            Detail = "This endpoint is scaffolded but not yet implemented. "
                + "Use the matching /api/mock/... endpoint for fake data during development.",
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

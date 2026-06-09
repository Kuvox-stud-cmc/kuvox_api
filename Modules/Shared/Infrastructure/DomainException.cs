using Microsoft.AspNetCore.Http;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// A business-rule failure carrying the HTTP status code to surface (404/403/409/400).
/// Mapped to an RFC 7807 response by <see cref="DomainExceptionHandler"/>. Mirrors Auth's
/// <c>AuthException</c> but is module-neutral so Projects/Media/Studios can share it.
/// </summary>
public sealed class DomainException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static DomainException NotFound(string message) => new(StatusCodes.Status404NotFound, message);

    public static DomainException Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);

    public static DomainException Conflict(string message) => new(StatusCodes.Status409Conflict, message);

    public static DomainException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
}

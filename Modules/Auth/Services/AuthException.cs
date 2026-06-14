namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Domain-level auth failure carrying the HTTP status code to surface (e.g. 401 for bad
/// credentials, 409 for a duplicate email). Mapped to RFC 7807 by <c>AuthExceptionHandler</c>.
/// </summary>
public sealed class AuthException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static AuthException Conflict(string message) => new(StatusCodes.Status409Conflict, message);

    public static AuthException Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message);

    public static AuthException Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);

    public static AuthException NotFound(string message) => new(StatusCodes.Status404NotFound, message);

    public static AuthException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
}

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Domain-level auth failure carrying the HTTP status code to surface (e.g. 401 for bad
/// credentials, 409 for a duplicate email). Mapped to RFC 7807 by <c>AuthExceptionHandler</c>.
/// </summary>
public sealed class AuthException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public static AuthException ActiveSessionConflict() =>
        new(
            StatusCodes.Status409Conflict,
            "active_session_conflict",
            "This account already has an active session.");

    public static AuthException Conflict(string message) =>
        new(StatusCodes.Status409Conflict, "authentication_conflict", message);

    public static AuthException Unauthorized(string message, string code = "authentication_unauthorized") =>
        new(StatusCodes.Status401Unauthorized, code, message);

    public static AuthException Forbidden(string message) =>
        new(StatusCodes.Status403Forbidden, "authentication_forbidden", message);

    public static AuthException NotFound(string message) =>
        new(StatusCodes.Status404NotFound, "authentication_not_found", message);

    public static AuthException BadRequest(string message) =>
        new(StatusCodes.Status400BadRequest, "authentication_bad_request", message);
}

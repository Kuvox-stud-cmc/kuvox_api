namespace Kuvox.Api.Modules.Auth.Contracts;

/// <summary>
/// Public cross-module API of the Auth module (Rule 2). Other modules depend on this
/// interface — never on Auth's services, repositories, or entities (Rule 1).
/// </summary>
public interface IAuthApi
{
    /// <summary>Returns <c>true</c> if a user with the given id exists.</summary>
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns a minimal, shareable view of a user, or <c>null</c> if not found.</summary>
    Task<UserSummary?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a user by email for sharing flows (look up an invitee). Email is matched
    /// case-insensitively. Returns <c>null</c> if no such user exists.
    /// </summary>
    Task<UserSummary?> GetSummaryByEmailAsync(string email, CancellationToken cancellationToken = default);
}

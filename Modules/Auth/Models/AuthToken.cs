using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

/// <summary>
/// A single-use, expiring token for email verification or password reset
/// (table <c>auth.auth_tokens</c>). Only the SHA-256 hash of the opaque token is stored — the
/// raw value travels in the emailed link. Consumed once (<see cref="UsedAt"/>) and superseded
/// when a fresh token of the same <see cref="Purpose"/> is issued.
/// </summary>
public sealed class AuthToken : BaseEntity
{
    public required Guid UserId { get; set; }

    public required AuthTokenPurpose Purpose { get; set; }

    public required string TokenHash { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public bool IsActive => UsedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

/// <summary>
/// A persisted, rotation-tracked refresh token (table <c>auth.refresh_tokens</c>). Only the
/// SHA-256 hash of the opaque token is stored — the raw value lives client-side. Supports
/// rotation (<see cref="ReplacedByTokenHash"/>) and revocation (<see cref="RevokedAt"/>).
/// </summary>
public sealed class RefreshToken : BaseEntity
{
    public required Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>EF Core implementation of <see cref="IUserRepository"/>. Internal (Rule 1).</summary>
internal sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await db.Users.AddAsync(user, cancellationToken);

    public async Task<IReadOnlyList<(Guid StudioId, UserStudioRole Role)>> GetStudioMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.UserStudios
            .Where(us => us.UserId == userId)
            .Select(us => new { us.StudioId, us.Role })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.StudioId, r.Role)).ToList();
    }

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryCreateSessionAsync(
        Guid userId,
        Guid sessionId,
        RefreshToken refreshToken,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = DateTimeOffset.UtcNow;

            var user = await db.Users
                .FromSqlInterpolated($"SELECT * FROM auth.users WHERE \"Id\" = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                return false;
            }

            if (user.ActiveSessionId is { } activeSessionId)
            {
                var activeTokenExists = await db.RefreshTokens.AnyAsync(
                    rt => rt.UserId == userId
                        && rt.SessionId == activeSessionId
                        && rt.RevokedAt == null
                        && rt.ExpiresAt > now,
                    cancellationToken);

                if (activeTokenExists)
                {
                    return false;
                }
            }

            user.ActiveSessionId = sessionId;
            user.UpdatedAt = now;

            refreshToken.UserId = userId;
            refreshToken.SessionId = sessionId;
            await db.RefreshTokens.AddAsync(refreshToken, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> TryRotateRefreshTokenAsync(
        Guid refreshTokenId,
        Guid sessionId,
        string replacementTokenHash,
        RefreshToken replacementToken,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var now = DateTimeOffset.UtcNow;

            var currentToken = await db.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM auth.refresh_tokens WHERE \"Id\" = {refreshTokenId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (currentToken is null
                || currentToken.SessionId != sessionId
                || currentToken.RevokedAt is not null
                || currentToken.ExpiresAt <= now)
            {
                return false;
            }

            var isCurrentSession = await db.Users.AnyAsync(
                u => u.Id == currentToken.UserId && u.ActiveSessionId == sessionId,
                cancellationToken);

            if (!isCurrentSession)
            {
                return false;
            }

            currentToken.RevokedAt = now;
            currentToken.ReplacedByTokenHash = replacementTokenHash;
            currentToken.UpdatedAt = now;

            replacementToken.UserId = currentToken.UserId;
            replacementToken.SessionId = sessionId;
            await db.RefreshTokens.AddAsync(replacementToken, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> IsActiveSessionAsync(
        Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userSessionMatches = await db.Users.AnyAsync(
            u => u.Id == userId && u.ActiveSessionId == sessionId,
            cancellationToken);

        return userSessionMatches
            && await db.RefreshTokens.AnyAsync(
                rt => rt.UserId == userId
                    && rt.SessionId == sessionId
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > now,
                cancellationToken);
    }

    public async Task RevokeSessionAsync(
        Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.SessionId == sessionId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(rt => rt.RevokedAt, now)
                    .SetProperty(rt => rt.UpdatedAt, now),
                cancellationToken);

        await db.Users
            .Where(u => u.Id == userId && u.ActiveSessionId == sessionId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.ActiveSessionId, (Guid?)null)
                    .SetProperty(u => u.UpdatedAt, now),
                cancellationToken);
    }

    public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(rt => rt.RevokedAt, now)
                    .SetProperty(rt => rt.UpdatedAt, now),
                cancellationToken);

        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.ActiveSessionId, (Guid?)null)
                    .SetProperty(u => u.UpdatedAt, now),
                cancellationToken);
    }

    public async Task AddAuthTokenAsync(AuthToken token, CancellationToken cancellationToken = default) =>
        await db.AuthTokens.AddAsync(token, cancellationToken);

    public async Task<AuthToken?> GetActiveAuthTokenByHashAsync(
        string tokenHash, AuthTokenPurpose purpose, CancellationToken cancellationToken = default) =>
        await db.AuthTokens
            .FirstOrDefaultAsync(
                at => at.TokenHash == tokenHash
                    && at.Purpose == purpose
                    && at.UsedAt == null
                    && at.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);

    public async Task InvalidateAuthTokensAsync(
        Guid userId, AuthTokenPurpose purpose, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.AuthTokens
            .Where(at => at.UserId == userId && at.Purpose == purpose && at.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(at => at.UsedAt, now), cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
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

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

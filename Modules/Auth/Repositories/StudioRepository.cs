using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>EF Core implementation of <see cref="IStudioRepository"/>. Internal (Rule 1).</summary>
internal sealed class StudioRepository(AuthDbContext db) : IStudioRepository
{
    public Task<Studio?> GetByIdAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, cancellationToken);

    public async Task<IReadOnlyList<(Studio Studio, UserStudioRole Role)>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from us in db.UserStudios
            join s in db.Studios on us.StudioId equals s.Id
            where us.UserId == userId
            orderby s.Name
            select new { Studio = s, us.Role }).ToListAsync(cancellationToken);

        return rows.Select(r => (r.Studio, r.Role)).ToList();
    }

    public Task<UserStudio?> GetMembershipAsync(Guid studioId, Guid userId, CancellationToken cancellationToken = default) =>
        db.UserStudios.FirstOrDefaultAsync(us => us.StudioId == studioId && us.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<(User User, UserStudioRole Role)>> ListMembersAsync(
        Guid studioId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from us in db.UserStudios
            join u in db.Users on us.UserId equals u.Id
            where us.StudioId == studioId
            orderby u.DisplayName
            select new { User = u, us.Role }).ToListAsync(cancellationToken);

        return rows.Select(r => (r.User, r.Role)).ToList();
    }

    public Task<int> CountAdminsAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        db.UserStudios.CountAsync(us => us.StudioId == studioId && us.Role == UserStudioRole.Admin, cancellationToken);

    public async Task AddStudioAsync(Studio studio, CancellationToken cancellationToken = default) =>
        await db.Studios.AddAsync(studio, cancellationToken);

    public async Task AddMembershipAsync(UserStudio membership, CancellationToken cancellationToken = default) =>
        await db.UserStudios.AddAsync(membership, cancellationToken);

    public void RemoveMembership(UserStudio membership) => db.UserStudios.Remove(membership);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public void RemoveStudio(Studio studio) => db.Studios.Remove(studio);
}

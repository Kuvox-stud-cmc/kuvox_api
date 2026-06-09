using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Projects.Repositories;

internal sealed class ProjectRepository(ProjectsDbContext db) : IProjectRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Project> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Projects
            .Where(p => p.OwnerKind == ownerKind && p.OwnerId == ownerId && p.DeletedAt == null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<(Project Project, ProjectRole Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // "Shared with me" = junction rows for the caller, excluding anything they already own.
        var query =
            from pu in db.ProjectUsers
            join p in db.Projects on pu.ProjectId equals p.Id
            where pu.UserId == userId && p.DeletedAt == null && p.OwnerId != userId
            orderby p.UpdatedAt descending
            select new { Project = p, pu.Role };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(r => (r.Project, r.Role)).ToList(), total);
    }

    public async Task<(IReadOnlyList<Project> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Projects
            .Where(p => p.OwnerKind == ownerKind && p.OwnerId == ownerId && p.DeletedAt != null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Project>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await db.Projects.Where(p => p.DeletedAt != null && p.DeletedAt < cutoff).ToListAsync(cancellationToken);

    public Task<ProjectUser?> GetProjectUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default) =>
        db.ProjectUsers.FirstOrDefaultAsync(pu => pu.ProjectId == projectId && pu.UserId == userId, cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await db.Projects.AddAsync(project, cancellationToken);

    public async Task AddProjectUserAsync(ProjectUser projectUser, CancellationToken cancellationToken = default) =>
        await db.ProjectUsers.AddAsync(projectUser, cancellationToken);

    public void RemoveProjectUser(ProjectUser projectUser) => db.ProjectUsers.Remove(projectUser);

    public void Remove(Project project) => db.Projects.Remove(project);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

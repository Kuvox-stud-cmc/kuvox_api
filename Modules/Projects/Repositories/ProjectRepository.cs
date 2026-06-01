using Kuvox.Api.Modules.Projects.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Projects.Repositories;

internal sealed class ProjectRepository(ProjectsDbContext db) : IProjectRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        await db.Projects.Where(p => p.OwnerId == ownerId).ToListAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await db.Projects.AddAsync(project, cancellationToken);

    public void Remove(Project project) => db.Projects.Remove(project);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

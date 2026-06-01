using Kuvox.Api.Modules.Projects.Models;

namespace Kuvox.Api.Modules.Projects.Repositories;

internal interface IProjectRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    void Remove(Project project);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

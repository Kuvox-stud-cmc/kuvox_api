using Kuvox.Api.Modules.Timelines.Models;

namespace Kuvox.Api.Modules.Timelines.Repositories;

internal interface ITimelineRepository
{
    Task<Timeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Timeline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

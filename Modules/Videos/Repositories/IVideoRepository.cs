using Kuvox.Api.Modules.Videos.Models;

namespace Kuvox.Api.Modules.Videos.Repositories;

internal interface IVideoRepository
{
    Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Video>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Video video, CancellationToken cancellationToken = default);

    /// <summary>Deletes every video belonging to a project (used on project deletion cleanup).</summary>
    Task<int> DeleteByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

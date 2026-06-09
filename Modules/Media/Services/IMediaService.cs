using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>
/// Module-internal business API of the Media module. Public only for the public controller's
/// DI; impl stays <c>internal</c> (Rule 1). The cross-module surface is <c>Media.Contracts</c>
/// (Rule 2). Symmetric with <c>IProjectService</c>: workspace listing, shared-with-me, trash,
/// sharing, and soft/permanent delete, all scoped by the JWT-derived workspace/caller.
/// </summary>
public interface IMediaService
{
    Task<PagedResult<MediaDto>> ListByWorkspaceAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaDto>> ListSharedWithMeAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaTrashItemDto>> ListTrashAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<MediaDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Registers an uploaded object as a media item (metadata/record only in Phase 2).</summary>
    Task<MediaDto> RegisterAsync(WorkspaceScope scope, CallerContext caller, RegisterMediaRequest request, CancellationToken cancellationToken = default);

    Task ShareAsync(Guid id, CallerContext caller, ShareMediaRequest request, CancellationToken cancellationToken = default);

    Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a trashed media item and fires <c>MediaDeletedEvent</c>.</summary>
    Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);
}

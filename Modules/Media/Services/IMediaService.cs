using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

public interface IMediaService
{
    Task<PagedResult<MediaDto>> ListByWorkspaceAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaDto>> ListSharedWithMeAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaTrashItemDto>> ListTrashAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<MediaDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<MediaDto> UploadRawAsync(WorkspaceScope scope, CallerContext caller, UploadMediaRequest request, CancellationToken cancellationToken = default);

    Task ShareAsync(Guid id, CallerContext caller, ShareMediaRequest request, CancellationToken cancellationToken = default);

    Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);
}

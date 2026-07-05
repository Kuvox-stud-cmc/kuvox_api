using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

public interface IAlbumService
{
    Task<AlbumDto> CreateAlbumAsync(WorkspaceScope scope, CallerContext caller, CreateAlbumDto request, CancellationToken cancellationToken = default);
    
    Task DeleteAlbumAsync(WorkspaceScope? scope, Guid albumId, CallerContext caller, CancellationToken cancellationToken = default);
    
    Task AddMediaToAlbumAsync(WorkspaceScope? scope, Guid albumId, IEnumerable<Guid> mediaIds, CallerContext caller, CancellationToken cancellationToken = default);

    Task AssignAudioCategoryAsync(WorkspaceScope scope, CallerContext caller, string category, IEnumerable<Guid> mediaIds, CancellationToken cancellationToken = default);
    
    Task DeleteMediaFromAlbumAsync(WorkspaceScope? scope, Guid albumId, IEnumerable<Guid> mediaIds, CallerContext caller, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(WorkspaceScope scope, CallerContext caller, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumDto>> ListSharedWithMeAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaDto>> ListAlbumMediaAsync(WorkspaceScope? scope, Guid albumId, CallerContext caller, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<AlbumDto> SetFavoriteAsync(Guid albumId, CallerContext caller, ToggleAlbumFavoriteRequest request, CancellationToken cancellationToken = default);

    Task ShareAsync(Guid albumId, CallerContext caller, ShareAlbumRequest request, CancellationToken cancellationToken = default);

    Task UnshareAsync(Guid albumId, CallerContext caller, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumAccessMemberDto>> ListAccessAsync(Guid albumId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumAccessMemberDto>> UpdateAccessAsync(Guid albumId, CallerContext caller, UpdateAlbumAccessRequest request, CancellationToken cancellationToken = default);
}

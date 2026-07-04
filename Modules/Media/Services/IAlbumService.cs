using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;

namespace Kuvox.Api.Modules.Media.Services;

public interface IAlbumService
{
    Task<AlbumDto> CreateAlbumAsync(CreateAlbumDto request, Guid userId, CancellationToken cancellationToken = default);
    
    Task DeleteAlbumAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default);
    
    Task AddMediaToAlbumAsync(Guid albumId, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default);

    Task AssignAudioCategoryAsync(string category, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default);
    
    Task DeleteMediaFromAlbumAsync(Guid albumId, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<PagedResult<MediaDto>> ListAlbumMediaAsync(Guid albumId, Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<AlbumDto> SetFavoriteAsync(Guid albumId, Guid userId, ToggleAlbumFavoriteRequest request, CancellationToken cancellationToken = default);
}

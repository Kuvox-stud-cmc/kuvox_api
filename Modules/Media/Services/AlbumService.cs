using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class AlbumService(
    IAlbumRepository albumRepository,
    IMediaRepository mediaRepository) : IAlbumService
{
    public async Task<AlbumDto> CreateAlbumAsync(CreateAlbumDto request, Guid userId, CancellationToken cancellationToken = default)
    {
        var album = new Album
        {
            Name = request.Name,
            Description = request.Description,
            Kind = request.Kind,
            MaterialSymbol = request.MaterialSymbol,
            IsDeleteAble = true
        };

        albumRepository.Add(album);
        
        var albumUser = new AlbumUser
        {
            AlbumId = album.Id,
            UserId = userId,
            Role = Permission.Owner,
            IsFavorite = false
        };
        
        albumRepository.AddAlbumUser(albumUser);
        await albumRepository.SaveChangesAsync(cancellationToken);

        return new AlbumDto(album.Id, album.Name, album.Description, album.Kind, album.MaterialSymbol, album.IsDeleteAble);
    }

    public async Task DeleteAlbumAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (!album.IsDeleteAble) throw DomainException.Forbidden("This album is created as default, so you can't delete this album");

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.Role != Permission.Owner)
            throw DomainException.Forbidden("Only the owner can delete the album");

        albumRepository.Remove(album);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMediaToAlbumAsync(Guid albumId, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.Role is not Permission.Owner and not Permission.Editor)
            throw DomainException.Forbidden("You do not have permission to add media to this album");

        foreach (var mediaId in mediaIds.Distinct())
        {
            // Verify media exists
            var media = await mediaRepository.GetByIdAsync(mediaId, cancellationToken)
                ?? throw DomainException.NotFound($"Media {mediaId} not found");

            // Verify album constraints
            if (album.Kind == AlbumKind.Photo && media is not Photo)
                throw DomainException.BadRequest("Cannot add non-photo media to a Photo Album");
            if (album.Kind == AlbumKind.Video && media is not Video)
                throw DomainException.BadRequest("Cannot add non-video media to a Video Album");
            if (album.Kind == AlbumKind.Audio && media is not Audio)
                throw DomainException.BadRequest("Cannot add non-audio media to an Audio Album");

            // Check if already in album
            var existing = await albumRepository.GetAlbumMediaAsync(albumId, mediaId, cancellationToken);
            if (existing != null) continue;

            AlbumMedia newAlbumMedia = media switch
            {
                Photo => new AlbumPhoto { AlbumId = albumId, MediaId = mediaId },
                Video => new AlbumVideo { AlbumId = albumId, MediaId = mediaId },
                Audio => new AlbumAudio { AlbumId = albumId, MediaId = mediaId },
                _ => throw DomainException.BadRequest("Unknown media type")
            };

            albumRepository.AddAlbumMedia(newAlbumMedia);
        }

        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMediaFromAlbumAsync(Guid albumId, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default)
    {
        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.Role is not Permission.Owner and not Permission.Editor)
            throw DomainException.Forbidden("You do not have permission to remove media from this album");

        foreach (var mediaId in mediaIds.Distinct())
        {
            var existing = await albumRepository.GetAlbumMediaAsync(albumId, mediaId, cancellationToken);
            if (existing != null)
            {
                albumRepository.RemoveAlbumMedia(existing);
            }
        }

        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var albums = await albumRepository.ListByUserAsync(userId, cancellationToken);
        return albums.Select(a => new AlbumDto(a.Id, a.Name, a.Description, a.Kind, a.MaterialSymbol, a.IsDeleteAble)).ToList();
    }

    public async Task<PagedResult<MediaDto>> ListAlbumMediaAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default)
    {
        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        var media = await albumRepository.ListAlbumMediaAsync(albumId, cancellationToken);
        var dtos = media.Select(MediaService.ToDto).ToList();
        
        // Return as PagedResult to match existing patterns, even though we just fetched all for now.
        return new PagedResult<MediaDto>(dtos, 1, dtos.Count > 0 ? dtos.Count : 1, dtos.Count);
    }
}

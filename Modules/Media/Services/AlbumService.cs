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
        if (request.Kind == AlbumKind.Audio && TryGetReservedAudioCategory(request.Name) is not null)
        {
            throw DomainException.BadRequest("This audio category name is reserved.");
        }

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

        return ToDto(album, albumUser.IsFavorite);
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

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.Role is not Permission.Owner and not Permission.Editor)
            throw DomainException.Forbidden("You do not have permission to add media to this album");

        await AddMediaToAlbumCoreAsync(album, mediaIds, cancellationToken);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignAudioCategoryAsync(
        string category,
        IEnumerable<Guid> mediaIds,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var categoryKey = TryGetReservedAudioCategory(category)
            ?? throw DomainException.BadRequest("Choose a valid audio category.");

        var album = await GetOrCreateSystemAudioCategoryAlbumAsync(userId, categoryKey, cancellationToken);
        await AddMediaToAlbumCoreAsync(album, mediaIds, cancellationToken);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMediaFromAlbumAsync(Guid albumId, IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

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

    public async Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default)
    {
        var albums = await albumRepository.ListByUserAsync(userId, includeSystem, cancellationToken);
        if (!includeSystem)
        {
            albums = albums
                .Where(album => album.Kind != AlbumKind.Audio || TryGetReservedAudioCategory(album.Name) is null)
                .ToList();
        }

        var flags = await albumRepository.GetFavoriteFlagsAsync(albums.Select(album => album.Id), userId, cancellationToken);
        return albums.Select(album => ToDto(album, flags.GetValueOrDefault(album.Id))).ToList();
    }

    public async Task<PagedResult<MediaDto>> ListAlbumMediaAsync(Guid albumId, Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (!includeSystem && !album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        var media = await albumRepository.ListAlbumMediaAsync(albumId, cancellationToken);
        var dtos = media.Select(item => MediaService.ToDto(item)).ToList();
        
        // Return as PagedResult to match existing patterns, even though we just fetched all for now.
        return new PagedResult<MediaDto>(dtos, 1, dtos.Count > 0 ? dtos.Count : 1, dtos.Count);
    }

    public async Task<AlbumDto> SetFavoriteAsync(
        Guid albumId,
        Guid userId,
        ToggleAlbumFavoriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, userId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.IsFavorite != request.IsFavorite)
        {
            albumUser.IsFavorite = request.IsFavorite;
            albumUser.UpdatedAt = DateTimeOffset.UtcNow;
            await albumRepository.SaveChangesAsync(cancellationToken);
        }

        return ToDto(album, albumUser.IsFavorite);
    }

    private static AlbumDto ToDto(Album album, bool isFavorite) =>
        new(album.Id, album.Name, album.Description, album.Kind, album.MaterialSymbol, album.IsDeleteAble, isFavorite);

    private async Task AddMediaToAlbumCoreAsync(
        Album album,
        IEnumerable<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        foreach (var mediaId in mediaIds.Distinct())
        {
            var media = await mediaRepository.GetByIdAsync(mediaId, cancellationToken)
                ?? throw DomainException.NotFound($"Media {mediaId} not found");

            if (media.DeletedAt is not null)
            {
                throw DomainException.NotFound($"Media {mediaId} not found");
            }

            if (album.Kind == AlbumKind.Photo && media is not Photo)
                throw DomainException.BadRequest("Cannot add non-photo media to a Photo Album");
            if (album.Kind == AlbumKind.Video && media is not Video)
                throw DomainException.BadRequest("Cannot add non-video media to a Video Album");
            if (album.Kind == AlbumKind.Audio && media is not Audio)
                throw DomainException.BadRequest("Cannot add non-audio media to an Audio Album");

            var existing = await albumRepository.GetAlbumMediaAsync(album.Id, mediaId, cancellationToken);
            if (existing != null) continue;

            AlbumMedia newAlbumMedia = media switch
            {
                Photo => new AlbumPhoto { AlbumId = album.Id, MediaId = mediaId },
                Video => new AlbumVideo { AlbumId = album.Id, MediaId = mediaId },
                Audio => new AlbumAudio { AlbumId = album.Id, MediaId = mediaId },
                _ => throw DomainException.BadRequest("Unknown media type")
            };

            albumRepository.AddAlbumMedia(newAlbumMedia);
        }
    }

    private async Task<Album> GetOrCreateSystemAudioCategoryAlbumAsync(
        Guid userId,
        AudioCategory category,
        CancellationToken cancellationToken)
    {
        var albums = await albumRepository.ListByUserAsync(userId, includeSystem: true, cancellationToken);
        var existing = albums
            .Where(album =>
                album.Kind == AlbumKind.Audio
                && !album.IsDeleteAble
                && TryGetReservedAudioCategory(album.Name) == category)
            .OrderBy(album => album.CreatedAt)
            .FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        var definition = DefinitionFor(category);
        var album = new Album
        {
            Name = definition.Name,
            Description = definition.Description,
            Kind = AlbumKind.Audio,
            MaterialSymbol = definition.MaterialSymbol,
            IsDeleteAble = false
        };

        albumRepository.Add(album);
        albumRepository.AddAlbumUser(new AlbumUser
        {
            AlbumId = album.Id,
            UserId = userId,
            Role = Permission.Owner,
            IsFavorite = false
        });

        return album;
    }

    private static AudioCategory? TryGetReservedAudioCategory(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return normalized switch
        {
            "music" => AudioCategory.Music,
            "soundeffect" or "soundeffects" or "sfx" => AudioCategory.SoundEffects,
            "voiceover" or "voiceovers" => AudioCategory.Voiceover,
            _ => null
        };
    }

    private static AudioCategoryDefinition DefinitionFor(AudioCategory category) =>
        category switch
        {
            AudioCategory.Music => new AudioCategoryDefinition(
                "Music",
                "Default Audio Album - Music",
                "music_note"),
            AudioCategory.SoundEffects => new AudioCategoryDefinition(
                "Sound Effects",
                "Default Audio Album - Sound Effects",
                "music_cast"),
            AudioCategory.Voiceover => new AudioCategoryDefinition(
                "Voiceover",
                "Default Audio Album - Voiceover",
                "record_voice_over"),
            _ => throw new NotSupportedException()
        };

    private enum AudioCategory
    {
        Music,
        SoundEffects,
        Voiceover
    }

    private sealed record AudioCategoryDefinition(string Name, string Description, string MaterialSymbol);
}

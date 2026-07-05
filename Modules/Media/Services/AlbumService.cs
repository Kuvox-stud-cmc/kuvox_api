using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class AlbumService(
    IAlbumRepository albumRepository,
    IMediaRepository mediaRepository,
    IAuthApi auth,
    INotificationsApi notifications) : IAlbumService
{
    public async Task<AlbumDto> CreateAlbumAsync(
        WorkspaceScope scope,
        CallerContext caller,
        CreateAlbumDto request,
        CancellationToken cancellationToken = default)
    {
        if (scope.IsStudio && !caller.CanWriteStudioContent(scope.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to create Studio albums.");
        }

        if (request.Kind == AlbumKind.Audio && TryGetReservedAudioCategory(request.Name) is not null)
        {
            throw DomainException.BadRequest("This audio category name is reserved.");
        }

        var album = new Album
        {
            OwnerId = scope.OwnerId,
            OwnerKind = OwnerKindOf(scope),
            Name = request.Name,
            Description = request.Description,
            Kind = request.Kind,
            MaterialSymbol = request.MaterialSymbol,
            IsDeleteAble = true
        };

        albumRepository.Add(album);

        if (!scope.IsStudio)
        {
            var albumUser = new AlbumUser
            {
                AlbumId = album.Id,
                UserId = caller.UserId,
                Role = Permission.Owner,
                IsFavorite = false,
                IsHidden = false
            };
            albumRepository.AddAlbumUser(albumUser);
        }

        await albumRepository.SaveChangesAsync(cancellationToken);

        return ToDto(album, false);
    }

    public async Task DeleteAlbumAsync(WorkspaceScope? scope, Guid albumId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        RequireAlbumInWorkspace(scope, album);

        if (!album.IsDeleteAble) throw DomainException.Forbidden("This album is created as default, so you can't delete this album");

        if (album.OwnerKind == OwnerKind.User)
        {
            var albumUser = await albumRepository.GetAlbumUserAsync(albumId, caller.UserId, cancellationToken)
                ?? throw DomainException.Forbidden("You do not have access to this album");

            if (albumUser.Role != Permission.Owner)
            {
                throw DomainException.Forbidden("Only the owner can delete the album");
            }
        }
        else
        {
            await RequireWriteAsync(album, caller, cancellationToken);
        }

        albumRepository.Remove(album);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMediaToAlbumAsync(WorkspaceScope? scope, Guid albumId, IEnumerable<Guid> mediaIds, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        RequireAlbumInWorkspace(scope, album);

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        await RequireWriteAsync(album, caller, cancellationToken);

        await AddMediaToAlbumCoreAsync(album, mediaIds, caller, cancellationToken);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignAudioCategoryAsync(
        WorkspaceScope scope,
        CallerContext caller,
        string category,
        IEnumerable<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        if (scope.IsStudio && !caller.CanWriteStudioContent(scope.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to update Studio audio categories.");
        }

        var categoryKey = TryGetReservedAudioCategory(category)
            ?? throw DomainException.BadRequest("Choose a valid audio category.");

        var album = await GetOrCreateSystemAudioCategoryAlbumAsync(scope, caller, categoryKey, cancellationToken);
        await AddMediaToAlbumCoreAsync(album, mediaIds, caller, cancellationToken);
        await albumRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMediaFromAlbumAsync(WorkspaceScope? scope, Guid albumId, IEnumerable<Guid> mediaIds, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        RequireAlbumInWorkspace(scope, album);

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        await RequireWriteAsync(album, caller, cancellationToken);

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

    public async Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(WorkspaceScope scope, CallerContext caller, bool includeSystem = false, CancellationToken cancellationToken = default)
    {
        var albums = await albumRepository.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, caller.UserId, includeSystem, cancellationToken);
        albums = albums
            .Where(album => album.OwnerKind == OwnerKindOf(scope) && album.OwnerId == scope.OwnerId)
            .ToList();
        if (!includeSystem)
        {
            albums = albums
                .Where(album => album.Kind != AlbumKind.Audio || TryGetReservedAudioCategory(album.Name) is null)
                .ToList();
        }

        if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
        {
            albums = await FilterVisibleAsync(albums, caller, cancellationToken);
        }

        var flags = scope.IsStudio
            ? new Dictionary<Guid, bool>()
            : await albumRepository.GetFavoriteFlagsAsync(albums.Select(album => album.Id), caller.UserId, cancellationToken);
        var mediaCounts = await albumRepository.GetMediaCountsAsync(albums.Select(album => album.Id), cancellationToken);
        return albums.Select(album => ToDto(album, flags.GetValueOrDefault(album.Id), mediaCounts.GetValueOrDefault(album.Id))).ToList();
    }

    public async Task<IReadOnlyList<AlbumDto>> ListSharedWithMeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await albumRepository.ListSharedWithUserAsync(userId, cancellationToken);
        var flags = await albumRepository.GetFavoriteFlagsAsync(items.Select(item => item.Album.Id), userId, cancellationToken);
        var mediaCounts = await albumRepository.GetMediaCountsAsync(items.Select(item => item.Album.Id), cancellationToken);
        var owners = await GetUserOwnerSummariesAsync(items.Select(item => item.Album), cancellationToken);
        return items.Select(item => ToDto(
            item.Album,
            flags.GetValueOrDefault(item.Album.Id),
            mediaCounts.GetValueOrDefault(item.Album.Id),
            owners.GetValueOrDefault(item.Album.OwnerId))).ToList();
    }

    public async Task<PagedResult<MediaDto>> ListAlbumMediaAsync(WorkspaceScope? scope, Guid albumId, CallerContext caller, bool includeSystem = false, CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        RequireAlbumInWorkspace(scope, album);

        if (!includeSystem && !album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        await RequireReadAsync(album, caller, cancellationToken);

        var media = await albumRepository.ListAlbumMediaAsync(albumId, cancellationToken);
        if (album.OwnerKind == OwnerKind.Studio && !caller.IsStudioOwner(album.OwnerId))
        {
            var visible = new List<Models.Media>();
            foreach (var item in media)
            {
                var access = await mediaRepository.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
                if (access?.IsHidden != true)
                {
                    visible.Add(item);
                }
            }

            media = visible;
        }
        else if (album.OwnerKind == OwnerKind.User && !caller.OwnsAsUser(album.OwnerId))
        {
            var visible = new List<Models.Media>();
            foreach (var item in media)
            {
                var access = await mediaRepository.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
                if (access?.IsHidden != true)
                {
                    visible.Add(item);
                }
            }

            media = visible;
        }

        var owners = await GetUserOwnerSummariesAsync(media, cancellationToken);
        var dtos = media.Select(item => MediaService.ToDto(item, owner: owners.GetValueOrDefault(item.OwnerId))).ToList();
        
        // Return as PagedResult to match existing patterns, even though we just fetched all for now.
        return new PagedResult<MediaDto>(dtos, 1, dtos.Count > 0 ? dtos.Count : 1, dtos.Count);
    }

    public async Task<AlbumDto> SetFavoriteAsync(
        Guid albumId,
        CallerContext caller,
        ToggleAlbumFavoriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (!album.IsDeleteAble)
        {
            throw DomainException.NotFound("Album not found");
        }

        if (album.OwnerKind != OwnerKind.User)
        {
            throw DomainException.Forbidden("Studio album favorites are not available yet.");
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(albumId, caller.UserId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.IsFavorite != request.IsFavorite)
        {
            albumUser.IsFavorite = request.IsFavorite;
            albumUser.UpdatedAt = DateTimeOffset.UtcNow;
            await albumRepository.SaveChangesAsync(cancellationToken);
        }

        var mediaCount = await GetMediaCountAsync(album.Id, cancellationToken);
        return ToDto(album, albumUser.IsFavorite, mediaCount);
    }

    public async Task ShareAsync(
        Guid albumId,
        CallerContext caller,
        ShareAlbumRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Role == Permission.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (album.OwnerKind != OwnerKind.User)
        {
            throw DomainException.BadRequest("Use Studio item access for Studio albums.");
        }

        await RequireWriteAsync(album, caller, cancellationToken);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (invitee.Id == album.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await albumRepository.GetAlbumUserAsync(album.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            albumRepository.AddAlbumUser(new AlbumUser
            {
                AlbumId = album.Id,
                UserId = invitee.Id,
                Role = request.Role,
                IsFavorite = false,
                IsHidden = false
            });
        }
        else
        {
            existing.Role = request.Role;
            existing.IsHidden = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await albumRepository.SaveChangesAsync(cancellationToken);
        await notifications.CreateAsync(
            invitee.Id,
            null,
            "MediaAccessChanged",
            $"An album was shared with you: {album.Name}.",
            "/dashboard/shared-assets",
            cancellationToken);
    }

    public async Task UnshareAsync(
        Guid albumId,
        CallerContext caller,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");

        if (album.OwnerKind != OwnerKind.User)
        {
            throw DomainException.BadRequest("Use Studio item access for Studio albums.");
        }

        await RequireWriteAsync(album, caller, cancellationToken);

        var share = await albumRepository.GetAlbumUserAsync(album.Id, userId, cancellationToken);
        if (share is not null)
        {
            albumRepository.RemoveAlbumUser(share);
            await albumRepository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AlbumAccessMemberDto>> ListAccessAsync(
        Guid albumId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");
        RequireStudioAccessManage(album, caller);
        return await BuildAccessRowsAsync(album, caller, cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumAccessMemberDto>> UpdateAccessAsync(
        Guid albumId,
        CallerContext caller,
        UpdateAlbumAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var album = await albumRepository.GetByIdAsync(albumId, cancellationToken)
            ?? throw DomainException.NotFound("Album not found");
        RequireStudioAccessManage(album, caller);
        var target = await auth.GetStudioMemberAsync(album.OwnerId, request.UserId, cancellationToken)
            ?? throw DomainException.NotFound("Studio member not found.");

        RequireCanManageTarget(caller, album.OwnerId, target.Role);
        var role = request.Role ?? DefaultPermissionForStudioRole(target.Role);
        if (role == Permission.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var access = await albumRepository.GetAlbumUserAsync(album.Id, request.UserId, cancellationToken);
        if (access is null)
        {
            albumRepository.AddAlbumUser(new AlbumUser
            {
                AlbumId = album.Id,
                UserId = request.UserId,
                Role = role,
                IsFavorite = false,
                IsHidden = request.IsHidden
            });
        }
        else
        {
            access.Role = role;
            access.IsHidden = request.IsHidden;
            access.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await albumRepository.SaveChangesAsync(cancellationToken);
        return await BuildAccessRowsAsync(album, caller, cancellationToken);
    }

    private async Task<int> GetMediaCountAsync(Guid albumId, CancellationToken cancellationToken)
    {
        var counts = await albumRepository.GetMediaCountsAsync([albumId], cancellationToken);
        return counts.GetValueOrDefault(albumId);
    }

    private async Task<IReadOnlyDictionary<Guid, UserSummary>> GetUserOwnerSummariesAsync(
        IEnumerable<Album> albums,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<Guid, UserSummary>();
        foreach (var ownerId in albums
            .Where(item => item.OwnerKind == OwnerKind.User)
            .Select(item => item.OwnerId)
            .Distinct())
        {
            var summary = await auth.GetSummaryAsync(ownerId, cancellationToken);
            if (summary is not null)
            {
                owners[ownerId] = summary;
            }
        }

        return owners;
    }

    private async Task<IReadOnlyDictionary<Guid, UserSummary>> GetUserOwnerSummariesAsync(
        IEnumerable<Models.Media> mediaItems,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<Guid, UserSummary>();
        foreach (var ownerId in mediaItems
            .Where(item => item.OwnerKind == OwnerKind.User)
            .Select(item => item.OwnerId)
            .Distinct())
        {
            var summary = await auth.GetSummaryAsync(ownerId, cancellationToken);
            if (summary is not null)
            {
                owners[ownerId] = summary;
            }
        }

        return owners;
    }

    private static AlbumDto ToDto(Album album, bool isFavorite, int mediaCount = 0, UserSummary? owner = null) =>
        new(
            album.Id,
            album.OwnerId,
            album.OwnerKind,
            owner?.Email,
            owner?.DisplayName,
            album.Name,
            album.Description,
            album.Kind,
            album.MaterialSymbol,
            album.IsDeleteAble,
            mediaCount,
            isFavorite);

    private async Task AddMediaToAlbumCoreAsync(
        Album album,
        IEnumerable<Guid> mediaIds,
        CallerContext caller,
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

            await RequireMediaAllowedInAlbumAsync(album, media, caller, cancellationToken);

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

    private async Task RequireReadAsync(Album album, CallerContext caller, CancellationToken cancellationToken)
    {
        if (album.OwnerKind == OwnerKind.Studio)
        {
            if (caller.IsStudioOwner(album.OwnerId))
            {
                return;
            }

            if (!caller.InStudio(album.OwnerId))
            {
                throw DomainException.Forbidden("You do not have access to this Studio album.");
            }

            var access = await albumRepository.GetAlbumUserAsync(album.Id, caller.UserId, cancellationToken);
            if (access?.IsHidden == true)
            {
                throw DomainException.Forbidden("You do not have access to this Studio album.");
            }

            return;
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(album.Id, caller.UserId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");
        if (albumUser.IsHidden)
        {
            throw DomainException.Forbidden("You do not have access to this album");
        }
    }

    private async Task RequireWriteAsync(Album album, CallerContext caller, CancellationToken cancellationToken)
    {
        if (album.OwnerKind == OwnerKind.Studio)
        {
            if (caller.IsStudioOwner(album.OwnerId))
            {
                return;
            }

            var access = await albumRepository.GetAlbumUserAsync(album.Id, caller.UserId, cancellationToken);
            if (access is { IsHidden: true } or { Role: Permission.Viewer })
            {
                throw DomainException.Forbidden("You do not have permission to modify this Studio album.");
            }

            if (access is { Role: Permission.Owner or Permission.Editor })
            {
                return;
            }

            if (!caller.CanWriteStudioContent(album.OwnerId))
            {
                throw DomainException.Forbidden("You do not have permission to modify this Studio album.");
            }

            return;
        }

        var albumUser = await albumRepository.GetAlbumUserAsync(album.Id, caller.UserId, cancellationToken)
            ?? throw DomainException.Forbidden("You do not have access to this album");

        if (albumUser.IsHidden || albumUser.Role is not Permission.Owner and not Permission.Editor)
        {
            throw DomainException.Forbidden("You do not have permission to modify this album");
        }
    }

    private async Task RequireMediaAllowedInAlbumAsync(
        Album album,
        Models.Media media,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        if (album.OwnerKind == OwnerKind.Studio)
        {
            if (media.OwnerKind != OwnerKind.Studio || media.OwnerId != album.OwnerId)
            {
                throw DomainException.BadRequest("Studio albums can only contain media from the same Studio.");
            }

            return;
        }

        if (media.OwnerKind != OwnerKind.User)
        {
            throw DomainException.BadRequest("Personal albums can only contain personal media.");
        }

        if (caller.OwnsAsUser(media.OwnerId))
        {
            return;
        }

        if (await mediaRepository.GetMediaUserAsync(media.Id, caller.UserId, cancellationToken) is not { IsHidden: false })
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }
    }

    private async Task<Album> GetOrCreateSystemAudioCategoryAlbumAsync(
        WorkspaceScope scope,
        CallerContext caller,
        AudioCategory category,
        CancellationToken cancellationToken)
    {
        var ownerKind = OwnerKindOf(scope);
        var albums = await albumRepository.ListByWorkspaceAsync(ownerKind, scope.OwnerId, caller.UserId, includeSystem: true, cancellationToken);
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
            OwnerId = scope.OwnerId,
            OwnerKind = ownerKind,
            Name = definition.Name,
            Description = definition.Description,
            Kind = AlbumKind.Audio,
            MaterialSymbol = definition.MaterialSymbol,
            IsDeleteAble = false
        };

        albumRepository.Add(album);

        if (!scope.IsStudio)
        {
            albumRepository.AddAlbumUser(new AlbumUser
            {
                AlbumId = album.Id,
                UserId = caller.UserId,
                Role = Permission.Owner,
                IsFavorite = false,
                IsHidden = false
            });
        }

        return album;
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static void RequireAlbumInWorkspace(WorkspaceScope? scope, Album album)
    {
        if (scope is null)
        {
            return;
        }

        var ownerKind = OwnerKindOf(scope);
        if (album.OwnerKind != ownerKind || album.OwnerId != scope.OwnerId)
        {
            throw DomainException.NotFound("Album not found");
        }
    }

    private async Task<IReadOnlyList<Album>> FilterVisibleAsync(
        IReadOnlyList<Album> albums,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var visible = new List<Album>();
        foreach (var album in albums)
        {
            var access = await albumRepository.GetAlbumUserAsync(album.Id, caller.UserId, cancellationToken);
            if (access?.IsHidden != true)
            {
                visible.Add(album);
            }
        }

        return visible;
    }

    private async Task<IReadOnlyList<AlbumAccessMemberDto>> BuildAccessRowsAsync(
        Album album,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(album.OwnerId, cancellationToken);
        var rows = new List<AlbumAccessMemberDto>();
        foreach (var member in members)
        {
            var access = await albumRepository.GetAlbumUserAsync(album.Id, member.UserId, cancellationToken);
            rows.Add(new AlbumAccessMemberDto(
                member.UserId,
                member.Email,
                member.DisplayName,
                member.Role,
                access?.Role ?? DefaultPermissionForStudioRole(member.Role),
                access?.Role,
                access?.IsHidden ?? false,
                CanManageTarget(caller, album.OwnerId, member.Role)));
        }

        return rows;
    }

    private static void RequireStudioAccessManage(Album album, CallerContext caller)
    {
        if (album.OwnerKind != OwnerKind.Studio)
        {
            throw DomainException.BadRequest("Item access overrides are only available for Studio albums.");
        }

        if (!caller.CanManageStudioAccess(album.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to manage item access.");
        }
    }

    private static void RequireCanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (!CanManageTarget(caller, studioId, targetRole))
        {
            throw DomainException.Forbidden("You cannot restrict a member with that Studio role.");
        }
    }

    private static bool CanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (caller.IsStudioOwner(studioId))
        {
            return !string.Equals(targetRole, "Owner", StringComparison.Ordinal);
        }

        return caller.IsStudioAdmin(studioId)
            && !string.Equals(targetRole, "Owner", StringComparison.Ordinal)
            && !string.Equals(targetRole, "Admin", StringComparison.Ordinal);
    }

    private static Permission DefaultPermissionForStudioRole(string studioRole) =>
        string.Equals(studioRole, "Viewer", StringComparison.Ordinal)
            ? Permission.Viewer
            : Permission.Editor;

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

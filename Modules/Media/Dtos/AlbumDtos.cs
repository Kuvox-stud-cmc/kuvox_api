using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Dtos;

public sealed record AlbumDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    string? OwnerEmail,
    string? OwnerDisplayName,
    string Name,
    string Description,
    AlbumKind Kind,
    string MaterialSymbol,
    bool IsDeleteAble,
    int MediaCount,
    bool IsFavorite
);

/// <summary>Creates a new album</summary>
public sealed record CreateAlbumDto(
    string Name,
    string Description,
    AlbumKind Kind,
    string MaterialSymbol
);

/// <summary>
/// Delete an album, only for the owner of the album.
/// </summary>
public sealed record DeleteAlbumDto(
    Guid AlbumId
);

/// <summary>
/// Add items to an album, only for owners and editors
/// </summary>
public sealed record AddMediaToAlbumDto(
    IEnumerable<Guid> MediaIds
);

public sealed record AssignAudioCategoryDto(
    IEnumerable<Guid> MediaIds
);

/// <summary>
/// Delete items from an album, only for owners and editors
/// </summary>
public sealed record DeleteMediaFromAlbumDto(
    IEnumerable<Guid> MediaIds
);

public sealed record ToggleAlbumFavoriteRequest(bool IsFavorite);

public sealed record ShareAlbumRequest(string Email, Permission Role);

public sealed record UpdateAlbumAccessRequest(Guid UserId, Permission? Role, bool IsHidden);

public sealed record AlbumAccessMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string StudioRole,
    Permission EffectiveRole,
    Permission? OverrideRole,
    bool IsHidden,
    bool CanManage);

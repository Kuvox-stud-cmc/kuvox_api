using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Dtos;

public sealed record AlbumDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    string Name,
    string Description,
    AlbumKind Kind,
    string MaterialSymbol,
    bool IsDeleteAble,
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

using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Contracts;

/// <summary>Shareable media projection for other modules (Rule 2).</summary>
public sealed record MediaSummary(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    MediaKind Kind,
    Guid? ProjectId,
    string Filename,
    string Status);

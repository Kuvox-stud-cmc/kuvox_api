using Kuvox.Api.Modules.Projects.Enums;
using MediaKind = Kuvox.Api.Modules.Media.Enums.MediaKind;
using System.Text.Json;
using Kuvox.Api.Modules.Shared.Dtos;

namespace Kuvox.Api.Modules.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    string? OwnerEmail,
    string? OwnerDisplayName,
    ProjectKind Kind,
    string Name,
    string? Description,
    double? DurationSeconds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MediaCount,
    bool IsStarred);

/// <summary>
/// Create payload. Owner (workspace + user) is derived from the JWT + <c>studioId</c> query —
/// never trusted from the body (Phase 2, plan §1).
/// </summary>
public sealed record CreateProjectRequest(ProjectKind Kind, string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, string Status);

/// <summary>Grants another user (looked up by email) access to a project.</summary>
public sealed record ShareProjectRequest(string Email, ProjectRole Role);

public sealed record UpdateProjectAccessRequest(Guid UserId, ProjectRole? Role, bool IsHidden);

public sealed record ProjectAccessMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string StudioRole,
    ProjectRole EffectiveRole,
    ProjectRole? OverrideRole,
    bool IsHidden,
    bool CanManage);

public sealed record ToggleProjectStarRequest(bool IsStarred);

public sealed record AttachProjectMediaRequest(IReadOnlyList<Guid> MediaIds);

public sealed record ProjectMediaDto(
    Guid MediaId,
    MediaKind? Kind,
    string Availability,
    string? Filename,
    Guid? OwnerId,
    Kuvox.Api.Modules.Media.Enums.OwnerKind? OwnerKind,
    string? Status,
    string? StorageKey,
    long? SizeBytes,
    string? CanonicalStorageKey,
    string? ProxyStorageKey,
    string? ThumbnailStorageKey,
    string? ErrorMessage,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string? Codec,
    double? FrameRate,
    DateTimeOffset? CreatedAt,
    long? SearchRevision);

/// <summary>A trashed project plus how long until auto-purge removes it (7-day window).</summary>
public sealed record ProjectTrashItemDto(
    Guid Id,
    ProjectKind Kind,
    string Name,
    string? Description,
    DateTimeOffset DeletedAt,
    int PurgesInDays);

public sealed record ImageCompositionDto(
    Guid ProjectId,
    JsonElement? DocumentJson,
    int RevisionNumber,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedByUserId);

public sealed record SaveImageCompositionRequest(
    JsonElement DocumentJson,
    JsonElement? OperationsJson,
    int BaseRevisionNumber);

public sealed record ImageCompositionRevisionDto(
    Guid Id,
    Guid ProjectId,
    int RevisionNumber,
    JsonElement DocumentJson,
    JsonElement OperationsJson,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId);

public sealed record EditorBootstrapTimelineDto(
    Guid ProjectId,
    Guid TimelineId,
    Guid RevisionId,
    JsonElement DocumentJson,
    int RevisionNumber,
    int DocumentSchemaVersion,
    string? Source,
    string? Label,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByUserId);

public sealed record ProjectEditorBootstrapDto(
    ProjectDto Project,
    PagedResult<ProjectMediaDto> ProjectMedia,
    EditorBootstrapTimelineDto? VideoTimeline,
    ImageCompositionDto? ImageComposition);

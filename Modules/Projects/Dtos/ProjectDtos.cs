using Kuvox.Api.Modules.Projects.Enums;

namespace Kuvox.Api.Modules.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    ProjectKind Kind,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Create payload. Owner (workspace + user) is derived from the JWT + <c>studioId</c> query —
/// never trusted from the body (Phase 2, plan §1).
/// </summary>
public sealed record CreateProjectRequest(ProjectKind Kind, string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, string Status);

/// <summary>Grants another user (looked up by email) access to a project.</summary>
public sealed record ShareProjectRequest(string Email, ProjectRole Role);

/// <summary>A trashed project plus how long until auto-purge removes it (7-day window).</summary>
public sealed record ProjectTrashItemDto(
    Guid Id,
    ProjectKind Kind,
    string Name,
    string? Description,
    DateTimeOffset DeletedAt,
    int PurgesInDays);

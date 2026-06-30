using Kuvox.Api.Modules.Auth.Enums;

namespace Kuvox.Api.Modules.Auth.Dtos;

/// <summary>A studio (team) the caller belongs to, plus the caller's role in it.</summary>
public sealed record StudioDto(Guid Id, string Name, UserStudioRole Role);

public sealed record CreateStudioRequest(string Name);

/// <summary>A studio member (joined with the user record for display).</summary>
public sealed record StudioMemberDto(Guid UserId, string Email, string DisplayName, UserStudioRole Role);

/// <summary>Invites a user by email to a studio.</summary>
public sealed record AddStudioMemberRequest(string Email, UserStudioRole Role);

/// <summary>Updates the role of an existing member (Admin-only).</summary>
public sealed record UpdateStudioMemberRequest(UserStudioRole Role);

/// <summary>Renames a studio (Admin-only).</summary>
public sealed record RenameStudioRequest(string Name);

public sealed record StudioInvitationDto(
    Guid Id,
    Guid StudioId,
    string Email,
    UserStudioRole Role,
    Guid InvitedByUserId,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? DeclinedAt,
    DateTimeOffset? RevokedAt);

public sealed record CreateStudioInvitationRequest(string Email, UserStudioRole Role);

public sealed record InvitationTokenRequest(string Token);

public sealed record StudioRoleDto(UserStudioRole Role, string Label, string Description, IReadOnlyList<string> Permissions);

public sealed record StudioPermissionDto(string Key, string Label, IReadOnlyList<UserStudioRole> Roles);

public sealed record StudioWorkspaceSettingsDto(Guid Id, string Name, string? Description, string? AvatarUrl, string? PublicSlug);

public sealed record UpdateStudioWorkspaceSettingsRequest(string Name, string? Description, string? AvatarUrl, string? PublicSlug);

public sealed record StudioNotificationSettingsDto(bool NotifyOnInvites, bool NotifyOnMembers, bool NotifyOnProjects, bool NotifyOnMedia);

public sealed record UpdateStudioNotificationSettingsRequest(bool NotifyOnInvites, bool NotifyOnMembers, bool NotifyOnProjects, bool NotifyOnMedia);

public sealed record StudioUsageSummaryDto(int MemberCount, int ProjectCount, int MediaCount, long StorageBytesUsed, long StorageBytesQuota);

public sealed record StudioAuditLogEntryDto(
    Guid Id,
    Guid? ActorUserId,
    string Category,
    string Action,
    string TargetKind,
    Guid? TargetId,
    string Summary,
    string? MetadataJson,
    DateTimeOffset CreatedAt);

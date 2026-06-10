using Kuvox.Api.Modules.Auth.Dtos;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Studio (team) management for the workspace switcher and team admin screens. Public only
/// for the controller's DI; impl stays <c>internal</c> (Rule 1). Authorization is checked
/// against persisted memberships (not the JWT claim) so a freshly created/changed team does
/// not require the caller to refresh their token first.
/// </summary>
public interface IStudioService
{
    /// <summary>Studios the caller belongs to (for the workspace switcher).</summary>
    Task<IReadOnlyList<StudioDto>> ListMineAsync(Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Creates a studio; the caller becomes its first <c>Admin</c>.</summary>
    Task<StudioDto> CreateAsync(Guid callerUserId, CreateStudioRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists a studio's members (any member may view).</summary>
    Task<IReadOnlyList<StudioMemberDto>> ListMembersAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Adds an existing user (by email) to the studio (Admin-only).</summary>
    Task<StudioMemberDto> AddMemberAsync(Guid studioId, Guid callerUserId, AddStudioMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes a member's role (Admin-only; cannot demote the last Admin).</summary>
    Task<StudioMemberDto> UpdateMemberAsync(Guid studioId, Guid callerUserId, Guid targetUserId, UpdateStudioMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a member (Admin-only; cannot remove the last Admin).</summary>
    Task RemoveMemberAsync(Guid studioId, Guid callerUserId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Renames a studio (Admin-only).</summary>
    Task<StudioDto> RenameAsync(Guid studioId, Guid callerUserId, RenameStudioRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a studio and all associated data (Admin-only).</summary>
    Task DeleteAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default);
}

using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Controllers;

/// <summary>
/// Studio (team) APIs: the caller's teams for the workspace switcher, studio creation, and
/// Admin-only member management. Authorization is enforced in <see cref="IStudioService"/>
/// against persisted memberships.
/// </summary>
[Authorize]
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class StudiosController(IStudioService studios) : ControllerBase
{
    /// <summary>Studios the caller belongs to (for the workspace switcher).</summary>
    [HttpGet("me/studios")]
    public Task<IReadOnlyList<StudioDto>> MyStudios(CancellationToken ct) =>
        studios.ListMineAsync(CallerId(), ct);

    /// <summary>Create a studio; the caller becomes its first Admin.</summary>
    [HttpPost("studios")]
    public Task<StudioDto> Create(CreateStudioRequest request, CancellationToken ct) =>
        studios.CreateAsync(CallerId(), request, ct);

    [HttpGet("studios/{studioId:guid}/members")]
    public Task<IReadOnlyList<StudioMemberDto>> Members(Guid studioId, CancellationToken ct) =>
        studios.ListMembersAsync(studioId, CallerId(), ct);

    [HttpPost("studios/{studioId:guid}/members")]
    public Task<StudioMemberDto> AddMember(Guid studioId, AddStudioMemberRequest request, CancellationToken ct) =>
        studios.AddMemberAsync(studioId, CallerId(), request, ct);

    [HttpPatch("studios/{studioId:guid}/members/{userId:guid}")]
    public Task<StudioMemberDto> UpdateMember(Guid studioId, Guid userId, UpdateStudioMemberRequest request, CancellationToken ct) =>
        studios.UpdateMemberAsync(studioId, CallerId(), userId, request, ct);

    [HttpDelete("studios/{studioId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid studioId, Guid userId, CancellationToken ct)
    {
        await studios.RemoveMemberAsync(studioId, CallerId(), userId, ct);
        return NoContent();
    }

    [HttpGet("studios/{studioId:guid}/invitations")]
    public Task<IReadOnlyList<StudioInvitationDto>> Invitations(Guid studioId, CancellationToken ct) =>
        studios.ListInvitationsAsync(studioId, CallerId(), ct);

    [HttpPost("studios/{studioId:guid}/invitations")]
    public Task<StudioInvitationDto> CreateInvitation(Guid studioId, CreateStudioInvitationRequest request, CancellationToken ct) =>
        studios.CreateInvitationAsync(studioId, CallerId(), request, ct);

    [HttpPost("studios/{studioId:guid}/invitations/{invitationId:guid}/resend")]
    public Task<StudioInvitationDto> ResendInvitation(Guid studioId, Guid invitationId, CancellationToken ct) =>
        studios.ResendInvitationAsync(studioId, CallerId(), invitationId, ct);

    [HttpDelete("studios/{studioId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid studioId, Guid invitationId, CancellationToken ct)
    {
        await studios.RevokeInvitationAsync(studioId, CallerId(), invitationId, ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation(InvitationTokenRequest request, CancellationToken ct)
    {
        await studios.AcceptInvitationAsync(request.Token, User.GetUserId(), ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("invitations/decline")]
    public async Task<IActionResult> DeclineInvitation(InvitationTokenRequest request, CancellationToken ct)
    {
        await studios.DeclineInvitationAsync(request.Token, ct);
        return NoContent();
    }

    [HttpGet("studios/{studioId:guid}/roles")]
    public Task<IReadOnlyList<StudioRoleDto>> Roles(Guid studioId, CancellationToken ct) =>
        studios.GetRolesAsync(studioId, CallerId(), ct);

    [HttpGet("studios/{studioId:guid}/permissions")]
    public Task<IReadOnlyList<StudioPermissionDto>> Permissions(Guid studioId, CancellationToken ct) =>
        studios.GetPermissionsAsync(studioId, CallerId(), ct);

    [HttpGet("studios/{studioId:guid}/audit-log")]
    public Task<PagedResult<StudioAuditLogEntryDto>> AuditLog(
        Guid studioId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] StudioAuditCategory? category = null,
        CancellationToken ct = default) =>
        studios.GetAuditLogAsync(studioId, CallerId(), page, pageSize, category, ct);

    [HttpGet("studios/{studioId:guid}/settings/workspace")]
    public Task<StudioWorkspaceSettingsDto> WorkspaceSettings(Guid studioId, CancellationToken ct) =>
        studios.GetWorkspaceSettingsAsync(studioId, CallerId(), ct);

    [HttpPatch("studios/{studioId:guid}/settings/workspace")]
    public Task<StudioWorkspaceSettingsDto> UpdateWorkspaceSettings(Guid studioId, UpdateStudioWorkspaceSettingsRequest request, CancellationToken ct) =>
        studios.UpdateWorkspaceSettingsAsync(studioId, CallerId(), request, ct);

    [HttpGet("studios/{studioId:guid}/settings/notifications")]
    public Task<StudioNotificationSettingsDto> NotificationSettings(Guid studioId, CancellationToken ct) =>
        studios.GetNotificationSettingsAsync(studioId, CallerId(), ct);

    [HttpPatch("studios/{studioId:guid}/settings/notifications")]
    public Task<StudioNotificationSettingsDto> UpdateNotificationSettings(Guid studioId, UpdateStudioNotificationSettingsRequest request, CancellationToken ct) =>
        studios.UpdateNotificationSettingsAsync(studioId, CallerId(), request, ct);

    [HttpGet("studios/{studioId:guid}/settings/storage")]
    [HttpGet("studios/{studioId:guid}/usage")]
    public Task<StudioUsageSummaryDto> Usage(Guid studioId, CancellationToken ct) =>
        studios.GetUsageSummaryAsync(studioId, CallerId(), ct);

    [HttpPatch("studios/{studioId:guid}")]
    public Task<StudioDto> RenameStudio(Guid studioId, [FromBody] RenameStudioRequest request, CancellationToken ct) =>
        studios.RenameAsync(studioId, CallerId(), request, ct);

    [HttpDelete("studios/{studioId:guid}")]
    public async Task<IActionResult> DeleteStudio(Guid studioId, CancellationToken ct)
    {
        await studios.DeleteAsync(studioId, CallerId(), ct);
        return NoContent();
    }

    private Guid CallerId() =>
        User.GetUserId() ?? throw DomainException.Forbidden("Invalid token.");
}

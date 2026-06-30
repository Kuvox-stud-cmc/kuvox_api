using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Default <see cref="IStudioService"/>. Resolves invitees by email through the user
/// repository, and authorizes against persisted memberships. Internal (Rule 1).
/// </summary>
internal sealed class StudioService(
    IStudioRepository studios,
    IUserRepository users,
    ITokenService tokens,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions,
    INotificationsApi notifications,
    MediatR.IMediator mediator) : IStudioService
{
    private const long DefaultStorageQuota = 25L * 1024L * 1024L * 1024L;
    private readonly string _frontendBaseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');
    public async Task<IReadOnlyList<StudioDto>> ListMineAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var rows = await studios.ListForUserAsync(callerUserId, cancellationToken);
        return rows.Select(r => new StudioDto(r.Studio.Id, r.Studio.Name, NormalizeRole(r.Role))).ToList();
    }

    public async Task<StudioDto> CreateAsync(Guid callerUserId, CreateStudioRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            throw DomainException.BadRequest("Studio name is required.");
        }

        var studio = new Studio { Name = name };
        await studios.AddStudioAsync(studio, cancellationToken);
        await studios.AddMembershipAsync(
            new UserStudio { UserId = callerUserId, StudioId = studio.Id, Role = UserStudioRole.Owner },
            cancellationToken);
        await AuditAsync(studio.Id, callerUserId, StudioAuditCategory.Workspace, "studio.created", "Studio", studio.Id, $"Created studio {studio.Name}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        return new StudioDto(studio.Id, studio.Name, UserStudioRole.Owner);
    }

    public async Task<StudioDto> RenameAsync(Guid studioID, Guid callerUserID, RenameStudioRequest request, CancellationToken cancellationToken = default) 
    {
        await RequireAdminAsync(studioID, callerUserID, cancellationToken);
        var newName = request.Name.Trim();
        if (newName.Length == 0) throw DomainException.BadRequest("Studio name is required");
        var studio = await studios.GetByIdAsync(studioID, cancellationToken) ?? throw DomainException.NotFound("Studio not found");

        studio.Name = newName;
        studio.UpdatedAt = DateTimeOffset.UtcNow;

        await studios.SaveChangesAsync(cancellationToken);

        var membership = await studios.GetMembershipAsync(studioID, callerUserID, cancellationToken);
        return new StudioDto(studio.Id, studio.Name, NormalizeRole(membership?.Role ?? UserStudioRole.Admin));
    }

    public async Task DeleteAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default) {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found");

        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Workspace, "studio.deleted", "Studio", studio.Id, $"Deleted studio {studio.Name}.", cancellationToken);
        studios.RemoveStudio(studio);
        await studios.SaveChangesAsync(cancellationToken);
        await mediator.Publish(new StudioDeletedEvent(studioId), cancellationToken);
    }

    public async Task<IReadOnlyList<StudioMemberDto>> ListMembersAsync(
        Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(studioId, callerUserId, cancellationToken);

        var rows = await studios.ListMembersAsync(studioId, cancellationToken);
        return rows.Select(r => new StudioMemberDto(r.User.Id, r.User.Email, r.User.DisplayName, NormalizeRole(r.Role))).ToList();
    }

    public async Task<StudioMemberDto> AddMemberAsync(
        Guid studioId, Guid callerUserId, AddStudioMemberRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);

        var invitee = await users.GetByEmailAsync(NormalizeEmail(request.Email), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email. Use the invitations endpoint for unregistered users.");

        var role = NormalizeRole(request.Role);

        var existing = await studios.GetMembershipAsync(studioId, invitee.Id, cancellationToken);
        if (existing is null)
        {
            await studios.AddMembershipAsync(
                new UserStudio { UserId = invitee.Id, StudioId = studioId, Role = role },
                cancellationToken);
        }
        else
        {
            await EnsureStudioKeepsPrivilegedMemberAsync(studioId, existing.Role, role, cancellationToken);
            existing.Role = role;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Members, "member.added", "User", invitee.Id, $"Added {invitee.Email} as {role}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);
        await notifications.CreateAsync(invitee.Id, studioId, "MemberJoined", "You were added to a Studio.", $"/teams/{studioId}", cancellationToken);
        return new StudioMemberDto(invitee.Id, invitee.Email, invitee.DisplayName, role);
    }

    public async Task<StudioMemberDto> UpdateMemberAsync(
        Guid studioId, Guid callerUserId, Guid targetUserId, UpdateStudioMemberRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);

        var membership = await studios.GetMembershipAsync(studioId, targetUserId, cancellationToken)
            ?? throw DomainException.NotFound("That user is not a member of this studio.");

        var role = NormalizeRole(request.Role);
        var callerMembership = await studios.GetMembershipAsync(studioId, callerUserId, cancellationToken);
        if (membership.Role == UserStudioRole.Owner && role != UserStudioRole.Owner)
        {
            throw DomainException.Forbidden("Studio Owners always keep the Owner role.");
        }

        if (role == UserStudioRole.Owner && callerMembership?.Role != UserStudioRole.Owner)
        {
            throw DomainException.Forbidden("Only a studio Owner can assign the Owner role.");
        }

        await EnsureStudioKeepsPrivilegedMemberAsync(studioId, membership.Role, role, cancellationToken);

        membership.Role = role;
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Members, "member.role_changed", "User", targetUserId, $"Changed member role to {role}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        var user = await users.GetByIdAsync(targetUserId, cancellationToken);
        await notifications.CreateAsync(targetUserId, studioId, "RoleChanged", $"Your Studio role changed to {role}.", $"/teams/{studioId}/members", cancellationToken);
        return new StudioMemberDto(targetUserId, user?.Email ?? string.Empty, user?.DisplayName ?? string.Empty, role);
    }

    public async Task RemoveMemberAsync(
        Guid studioId, Guid callerUserId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);

        var membership = await studios.GetMembershipAsync(studioId, targetUserId, cancellationToken);
        if (membership is null)
        {
            return;
        }

        if (callerUserId == targetUserId && membership.Role == UserStudioRole.Owner)
        {
            throw DomainException.Forbidden("Studio Owners cannot remove their own account from the Studio.");
        }

        await EnsureStudioKeepsPrivilegedMemberAsync(studioId, membership.Role, null, cancellationToken);

        studios.RemoveMembership(membership);
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Members, "member.removed", "User", targetUserId, "Removed a studio member.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);
        await notifications.CreateAsync(targetUserId, studioId, "MemberRemoved", "You were removed from a Studio.", null, cancellationToken);
    }

    public async Task<IReadOnlyList<StudioInvitationDto>> ListInvitationsAsync(
        Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var invitations = await studios.ListInvitationsAsync(studioId, cancellationToken);
        return invitations.Select(ToInvitationDto).ToList();
    }

    public async Task<StudioInvitationDto> CreateInvitationAsync(
        Guid studioId, Guid callerUserId, CreateStudioInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var callerMembership = await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        var email = NormalizeEmail(request.Email);
        var role = NormalizeRole(request.Role);

        if (role == UserStudioRole.Owner && callerMembership.Role != UserStudioRole.Owner)
        {
            throw DomainException.Forbidden("Only a studio Owner can invite another Owner.");
        }

        var existingUser = await users.GetByEmailAsync(email, cancellationToken);
        if (existingUser is not null && await studios.GetMembershipAsync(studioId, existingUser.Id, cancellationToken) is not null)
        {
            throw DomainException.Conflict("That user is already a studio member.");
        }

        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(TimeSpan.FromDays(Math.Clamp(studio.InvitationExpiryDays, 1, 30)));
        var invitation = new StudioInvitation
        {
            StudioId = studioId,
            Email = email,
            Role = role,
            InvitedByUserId = callerUserId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        };

        await studios.AddInvitationAsync(invitation, cancellationToken);
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Invitations, "invitation.sent", "Invitation", invitation.Id, $"Invited {email} as {role}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        await SendInvitationEmailAsync(studio, invitation, rawToken, cancellationToken);
        if (existingUser is not null)
        {
            await notifications.CreateAsync(existingUser.Id, studioId, "InvitationReceived", $"You were invited to {studio.Name}.", $"/invitations/accept?token={rawToken}", cancellationToken);
        }

        return ToInvitationDto(invitation);
    }

    public async Task<StudioInvitationDto> ResendInvitationAsync(
        Guid studioId, Guid callerUserId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        var invitation = await studios.GetInvitationAsync(studioId, invitationId, cancellationToken)
            ?? throw DomainException.NotFound("Invitation not found.");

        if (invitation.Status != StudioInvitationStatus.Pending)
        {
            throw DomainException.Conflict("Only pending invitations can be resent.");
        }

        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(TimeSpan.FromDays(Math.Clamp(studio.InvitationExpiryDays, 1, 30)));
        invitation.TokenHash = tokenHash;
        invitation.ExpiresAt = expiresAt;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Invitations, "invitation.resent", "Invitation", invitation.Id, $"Resent invitation to {invitation.Email}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        await SendInvitationEmailAsync(studio, invitation, rawToken, cancellationToken);
        return ToInvitationDto(invitation);
    }

    public async Task RevokeInvitationAsync(
        Guid studioId, Guid callerUserId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var invitation = await studios.GetInvitationAsync(studioId, invitationId, cancellationToken)
            ?? throw DomainException.NotFound("Invitation not found.");

        if (invitation.Status != StudioInvitationStatus.Pending)
        {
            return;
        }

        invitation.Status = StudioInvitationStatus.Revoked;
        invitation.RevokedAt = DateTimeOffset.UtcNow;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Invitations, "invitation.revoked", "Invitation", invitation.Id, $"Revoked invitation to {invitation.Email}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        var existingUser = await users.GetByEmailAsync(invitation.Email, cancellationToken);
        if (existingUser is not null)
        {
            await notifications.CreateAsync(existingUser.Id, studioId, "InvitationRevoked", "A Studio invitation was revoked.", null, cancellationToken);
        }
    }

    public async Task AcceptInvitationAsync(string token, Guid? callerUserId = null, CancellationToken cancellationToken = default)
    {
        var invitation = await LoadActiveInvitationAsync(token, cancellationToken);
        var user = callerUserId is { } id
            ? await users.GetByIdAsync(id, cancellationToken)
            : await users.GetByEmailAsync(invitation.Email, cancellationToken);

        if (user is null)
        {
            throw DomainException.BadRequest("Create and verify an account with the invited email before accepting this invitation.");
        }

        if (!string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw DomainException.Forbidden("This invitation is for a different email address.");
        }

        if (user.EmailVerifiedAt is null)
        {
            throw DomainException.Forbidden("Verify your email before accepting this invitation.");
        }

        await AcceptInvitationForUserAsync(invitation, user, cancellationToken);
    }

    public async Task DeclineInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await LoadActiveInvitationAsync(token, cancellationToken);
        invitation.Status = StudioInvitationStatus.Declined;
        invitation.DeclinedAt = DateTimeOffset.UtcNow;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(invitation.StudioId, null, StudioAuditCategory.Invitations, "invitation.declined", "Invitation", invitation.Id, $"Invitation declined by {invitation.Email}.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ClaimPendingInvitationsForUserAsync(Guid userId, string email, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken) ?? throw DomainException.NotFound("User not found.");
        if (user.EmailVerifiedAt is null)
        {
            return 0;
        }

        var invitations = await studios.ListClaimableInvitationsAsync(NormalizeEmail(email), cancellationToken);
        var accepted = 0;
        foreach (var invitation in invitations)
        {
            await AcceptInvitationForUserAsync(invitation, user, cancellationToken, save: false);
            accepted++;
        }

        if (accepted > 0)
        {
            await studios.SaveChangesAsync(cancellationToken);
        }

        return accepted;
    }

    public async Task<IReadOnlyList<StudioRoleDto>> GetRolesAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(studioId, callerUserId, cancellationToken);
        return RoleMatrix;
    }

    public async Task<IReadOnlyList<StudioPermissionDto>> GetPermissionsAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(studioId, callerUserId, cancellationToken);
        return PermissionMatrix;
    }

    public async Task<PagedResult<StudioAuditLogEntryDto>> GetAuditLogAsync(
        Guid studioId,
        Guid callerUserId,
        int page,
        int pageSize,
        StudioAuditCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        (page, pageSize) = Normalize(page, pageSize);
        var result = await studios.ListAuditLogAsync(studioId, page, pageSize, category, cancellationToken);
        return new PagedResult<StudioAuditLogEntryDto>(result.Items.Select(ToAuditDto).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<StudioWorkspaceSettingsDto> GetWorkspaceSettingsAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        return ToWorkspaceSettingsDto(studio);
    }

    public async Task<StudioWorkspaceSettingsDto> UpdateWorkspaceSettingsAsync(
        Guid studioId,
        Guid callerUserId,
        UpdateStudioWorkspaceSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            throw DomainException.BadRequest("Studio name is required.");
        }

        studio.Name = name;
        studio.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        studio.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        studio.PublicSlug = string.IsNullOrWhiteSpace(request.PublicSlug) ? null : request.PublicSlug.Trim().ToLowerInvariant();
        studio.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Settings, "settings.workspace_updated", "Studio", studioId, "Updated studio workspace settings.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);
        return ToWorkspaceSettingsDto(studio);
    }

    public async Task<StudioNotificationSettingsDto> GetNotificationSettingsAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        return new StudioNotificationSettingsDto(studio.NotifyOnInvites, studio.NotifyOnMembers, studio.NotifyOnProjects, studio.NotifyOnMedia);
    }

    public async Task<StudioNotificationSettingsDto> UpdateNotificationSettingsAsync(
        Guid studioId,
        Guid callerUserId,
        UpdateStudioNotificationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found.");
        studio.NotifyOnInvites = request.NotifyOnInvites;
        studio.NotifyOnMembers = request.NotifyOnMembers;
        studio.NotifyOnProjects = request.NotifyOnProjects;
        studio.NotifyOnMedia = request.NotifyOnMedia;
        studio.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(studioId, callerUserId, StudioAuditCategory.Settings, "settings.notifications_updated", "Studio", studioId, "Updated studio notification settings.", cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);
        return new StudioNotificationSettingsDto(studio.NotifyOnInvites, studio.NotifyOnMembers, studio.NotifyOnProjects, studio.NotifyOnMedia);
    }

    public async Task<StudioUsageSummaryDto> GetUsageSummaryAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var memberCount = await studios.CountMembersAsync(studioId, cancellationToken);
        return new StudioUsageSummaryDto(memberCount, ProjectCount: 0, MediaCount: 0, StorageBytesUsed: 0, StorageBytesQuota: DefaultStorageQuota);
    }

    private async Task<UserStudio> RequireMembershipAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (await studios.GetByIdAsync(studioId, cancellationToken) is null)
        {
            throw DomainException.NotFound("Studio not found.");
        }

        return await studios.GetMembershipAsync(studioId, callerUserId, cancellationToken)
            ?? throw DomainException.Forbidden("You are not a member of this studio.");
    }

    private async Task<UserStudio> RequireAdminAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(studioId, callerUserId, cancellationToken);
        if (!IsPrivileged(membership.Role))
        {
            throw DomainException.Forbidden("Only a studio Owner or Admin can perform this action.");
        }

        return membership;
    }

    private async Task AcceptInvitationForUserAsync(
        StudioInvitation invitation,
        User user,
        CancellationToken cancellationToken,
        bool save = true)
    {
        var existing = await studios.GetMembershipAsync(invitation.StudioId, user.Id, cancellationToken);
        if (existing is null)
        {
            await studios.AddMembershipAsync(new UserStudio
            {
                UserId = user.Id,
                StudioId = invitation.StudioId,
                Role = NormalizeRole(invitation.Role),
            }, cancellationToken);
        }
        else
        {
            existing.Role = NormalizeRole(invitation.Role);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        invitation.Status = StudioInvitationStatus.Accepted;
        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        invitation.UpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(invitation.StudioId, user.Id, StudioAuditCategory.Invitations, "invitation.accepted", "Invitation", invitation.Id, $"Invitation accepted by {invitation.Email}.", cancellationToken);

        if (save)
        {
            await studios.SaveChangesAsync(cancellationToken);
        }

        await notifications.CreateAsync(user.Id, invitation.StudioId, "MemberJoined", "You joined a Studio.", $"/teams/{invitation.StudioId}", cancellationToken);
        await notifications.CreateAsync(invitation.InvitedByUserId, invitation.StudioId, "InvitationAccepted", $"{user.DisplayName} accepted a Studio invitation.", $"/teams/{invitation.StudioId}/members", cancellationToken);
    }

    private async Task<StudioInvitation> LoadActiveInvitationAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw DomainException.BadRequest("Invitation token is required.");
        }

        var invitation = await studios.GetInvitationByTokenHashAsync(tokens.HashToken(token), cancellationToken)
            ?? throw DomainException.BadRequest("Invalid or expired invitation token.");

        if (invitation.Status != StudioInvitationStatus.Pending)
        {
            throw DomainException.Conflict("This invitation is no longer pending.");
        }

        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            invitation.Status = StudioInvitationStatus.Expired;
            invitation.UpdatedAt = DateTimeOffset.UtcNow;
            await studios.SaveChangesAsync(cancellationToken);
            throw DomainException.BadRequest("Invitation token has expired.");
        }

        return invitation;
    }

    private async Task EnsureStudioKeepsPrivilegedMemberAsync(
        Guid studioId, UserStudioRole currentRole, UserStudioRole? nextRole, CancellationToken cancellationToken)
    {
        if (IsPrivileged(currentRole) && (nextRole is null || !IsPrivileged(nextRole.Value))
            && await studios.CountPrivilegedMembersAsync(studioId, cancellationToken) <= 1)
        {
            throw DomainException.Conflict("A studio must keep at least one Owner or Admin.");
        }
    }

    private async Task AuditAsync(
        Guid studioId,
        Guid? actorUserId,
        StudioAuditCategory category,
        string action,
        string targetKind,
        Guid? targetId,
        string summary,
        CancellationToken cancellationToken)
    {
        await studios.AddAuditEntryAsync(new AuditLogEntry
        {
            ActorUserId = actorUserId,
            WorkspaceKind = "Studio",
            WorkspaceId = studioId,
            Category = category,
            Action = action,
            TargetKind = targetKind,
            TargetId = targetId,
            Summary = summary,
        }, cancellationToken);
    }

    private async Task SendInvitationEmailAsync(
        Studio studio, StudioInvitation invitation, string rawToken, CancellationToken cancellationToken)
    {
        var inviteLink = $"{_frontendBaseUrl}/invitations/accept?token={rawToken}";
        await emailSender.SendAsync(
            invitation.Email,
            $"You're invited to {studio.Name} on Kuvox",
            $"""
            <p>You have been invited to join {studio.Name} as {invitation.Role}.</p>
            <p><a href="{inviteLink}">{inviteLink}</a></p>
            <p>This invitation expires on {invitation.ExpiresAt:u}.</p>
            """,
            cancellationToken);
    }

    private static StudioInvitationDto ToInvitationDto(StudioInvitation invitation) =>
        new(
            invitation.Id,
            invitation.StudioId,
            invitation.Email,
            NormalizeRole(invitation.Role),
            invitation.InvitedByUserId,
            invitation.Status.ToString(),
            invitation.ExpiresAt,
            invitation.CreatedAt,
            invitation.AcceptedAt,
            invitation.DeclinedAt,
            invitation.RevokedAt);

    private static StudioAuditLogEntryDto ToAuditDto(AuditLogEntry entry) =>
        new(entry.Id, entry.ActorUserId, entry.Category.ToString(), entry.Action, entry.TargetKind, entry.TargetId, entry.Summary, entry.MetadataJson, entry.CreatedAt);

    private static StudioWorkspaceSettingsDto ToWorkspaceSettingsDto(Studio studio) =>
        new(studio.Id, studio.Name, studio.Description, studio.AvatarUrl, studio.PublicSlug);

    private static string NormalizeEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || !normalized.Contains('@'))
        {
            throw DomainException.BadRequest("A valid email is required.");
        }

        return normalized;
    }

    private static UserStudioRole NormalizeRole(UserStudioRole role) =>
        role == UserStudioRole.User ? UserStudioRole.Member : role;

    private static bool IsPrivileged(UserStudioRole role) =>
        NormalizeRole(role) is UserStudioRole.Owner or UserStudioRole.Admin;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static readonly IReadOnlyList<StudioRoleDto> RoleMatrix =
    [
        new(UserStudioRole.Owner, "Owner", "Full Studio control, including ownership and deletion.", ["studio.read", "studio.write", "access.manage", "content.write", "trash.manage"]),
        new(UserStudioRole.Admin, "Admin", "Manage Studio settings, members, invitations, and content.", ["studio.read", "studio.write", "access.manage", "content.write", "trash.manage"]),
        new(UserStudioRole.Member, "Member", "Create and edit Studio projects and media.", ["studio.read", "content.write"]),
        new(UserStudioRole.Viewer, "Viewer", "Read-only Studio access.", ["studio.read"])
    ];

    private static readonly IReadOnlyList<StudioPermissionDto> PermissionMatrix =
    [
        new("studio.read", "Read Studio projects and media.", [UserStudioRole.Owner, UserStudioRole.Admin, UserStudioRole.Member, UserStudioRole.Viewer]),
        new("studio.write", "Update Studio settings.", [UserStudioRole.Owner, UserStudioRole.Admin]),
        new("access.manage", "Manage members, roles, invitations, and audit log.", [UserStudioRole.Owner, UserStudioRole.Admin]),
        new("content.write", "Create and edit Studio projects and media.", [UserStudioRole.Owner, UserStudioRole.Admin, UserStudioRole.Member]),
        new("trash.manage", "Restore and permanently delete Studio trash.", [UserStudioRole.Owner, UserStudioRole.Admin])
    ];
}

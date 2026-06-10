using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Default <see cref="IStudioService"/>. Resolves invitees by email through the user
/// repository, and authorizes against persisted memberships. Internal (Rule 1).
/// </summary>
internal sealed class StudioService(IStudioRepository studios, IUserRepository users, MediatR.IMediator mediator) : IStudioService
{
    public async Task<IReadOnlyList<StudioDto>> ListMineAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var rows = await studios.ListForUserAsync(callerUserId, cancellationToken);
        return rows.Select(r => new StudioDto(r.Studio.Id, r.Studio.Name, r.Role)).ToList();
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
            new UserStudio { UserId = callerUserId, StudioId = studio.Id, Role = UserStudioRole.Admin },
            cancellationToken);
        await studios.SaveChangesAsync(cancellationToken);

        return new StudioDto(studio.Id, studio.Name, UserStudioRole.Admin);
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

        return new StudioDto(studio.Id, studio.Name, UserStudioRole.Admin);
    }

    public async Task DeleteAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default) {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);
        var studio = await studios.GetByIdAsync(studioId, cancellationToken) ?? throw DomainException.NotFound("Studio not found");

        studios.RemoveStudio(studio);
        await studios.SaveChangesAsync(cancellationToken);
        await mediator.Publish(new StudioDeletedEvent(studioId), cancellationToken);
    }

    public async Task<IReadOnlyList<StudioMemberDto>> ListMembersAsync(
        Guid studioId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(studioId, callerUserId, cancellationToken);

        var rows = await studios.ListMembersAsync(studioId, cancellationToken);
        return rows.Select(r => new StudioMemberDto(r.User.Id, r.User.Email, r.User.DisplayName, r.Role)).ToList();
    }

    public async Task<StudioMemberDto> AddMemberAsync(
        Guid studioId, Guid callerUserId, AddStudioMemberRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);

        var invitee = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        var existing = await studios.GetMembershipAsync(studioId, invitee.Id, cancellationToken);
        if (existing is null)
        {
            await studios.AddMembershipAsync(
                new UserStudio { UserId = invitee.Id, StudioId = studioId, Role = request.Role },
                cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await studios.SaveChangesAsync(cancellationToken);
        return new StudioMemberDto(invitee.Id, invitee.Email, invitee.DisplayName, request.Role);
    }

    public async Task<StudioMemberDto> UpdateMemberAsync(
        Guid studioId, Guid callerUserId, Guid targetUserId, UpdateStudioMemberRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(studioId, callerUserId, cancellationToken);

        var membership = await studios.GetMembershipAsync(studioId, targetUserId, cancellationToken)
            ?? throw DomainException.NotFound("That user is not a member of this studio.");

        // Don't allow demoting away the last remaining admin (would orphan the studio).
        if (membership.Role == UserStudioRole.Admin && request.Role != UserStudioRole.Admin
            && await studios.CountAdminsAsync(studioId, cancellationToken) <= 1)
        {
            throw DomainException.Conflict("A studio must keep at least one Admin.");
        }

        membership.Role = request.Role;
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        await studios.SaveChangesAsync(cancellationToken);

        var user = await users.GetByIdAsync(targetUserId, cancellationToken);
        return new StudioMemberDto(targetUserId, user?.Email ?? string.Empty, user?.DisplayName ?? string.Empty, request.Role);
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

        if (membership.Role == UserStudioRole.Admin
            && await studios.CountAdminsAsync(studioId, cancellationToken) <= 1)
        {
            throw DomainException.Conflict("A studio must keep at least one Admin.");
        }

        studios.RemoveMembership(membership);
        await studios.SaveChangesAsync(cancellationToken);
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

    private async Task RequireAdminAsync(Guid studioId, Guid callerUserId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(studioId, callerUserId, cancellationToken);
        if (membership.Role != UserStudioRole.Admin)
        {
            throw DomainException.Forbidden("Only a studio Admin can perform this action.");
        }
    }
}

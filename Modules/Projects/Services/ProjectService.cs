using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using MediatR;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Real Projects business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Persists via
/// <see cref="IProjectRepository"/>, resolves invitees through the Auth public contract
/// (<see cref="IAuthApi"/>, Rule 2), and publishes <see cref="ProjectDeletedEvent"/> on
/// permanent delete so Timelines/Media clean up (Rule 4).
/// </summary>
internal sealed class ProjectService(IProjectRepository projects, IAuthApi auth, IMediator mediator)
    : IProjectService
{
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    public async Task<PagedResult<ProjectDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<ProjectDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<ProjectDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListSharedWithUserAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<ProjectDto>(items.Select(x => ToDto(x.Project)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<ProjectTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<ProjectTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
    }

    public async Task<ProjectDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        return ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(
        WorkspaceScope scope, CallerContext caller, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        // Personal workspaces must reference a real user; studio membership was already
        // authorized via the JWT claim when the controller resolved the scope.
        if (!scope.IsStudio && !await auth.UserExistsAsync(scope.OwnerId, cancellationToken))
        {
            throw DomainException.BadRequest("Unknown owner.");
        }

        var project = new Project
        {
            OwnerId = scope.OwnerId,
            OwnerKind = OwnerKindOf(scope),
            Kind = request.Kind,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
        };

        await projects.AddAsync(project, cancellationToken);
        await projects.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    public async Task<ProjectDto> UpdateAsync(
        Guid id, CallerContext caller, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireManage(project, caller);

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.Status = request.Status;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    public async Task ShareAsync(
        Guid id, CallerContext caller, ShareProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireManage(project, caller);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (project.OwnerKind == OwnerKind.User && invitee.Id == project.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await projects.GetProjectUserAsync(project.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            await projects.AddProjectUserAsync(
                new ProjectUser { ProjectId = project.Id, UserId = invitee.Id, Role = request.Role },
                cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await projects.SaveChangesAsync(cancellationToken);
    }

    public async Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireManage(project, caller);

        var share = await projects.GetProjectUserAsync(project.Id, userId, cancellationToken);
        if (share is not null)
        {
            projects.RemoveProjectUser(share);
            await projects.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireManage(project, caller);

        project.DeletedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        RequireManage(project, caller);

        project.DeletedAt = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        RequireManage(project, caller);

        projects.Remove(project);
        await projects.SaveChangesAsync(cancellationToken);

        // Cascade cleanup to Timelines/Media (Rule 4).
        await mediator.Publish(new ProjectDeletedEvent(project.Id), cancellationToken);
    }

    private async Task<Project> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken);
        return project is null || project.DeletedAt is not null
            ? throw DomainException.NotFound("Project not found.")
            : project;
    }

    /// <summary>Owner of the workspace: the user (Personal) or any studio member (Team).</summary>
    private static bool CanManage(Project project, CallerContext caller) =>
        project.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(project.OwnerId)
            : caller.InStudio(project.OwnerId);

    private static void RequireManage(Project project, CallerContext caller)
    {
        if (!CanManage(project, caller))
        {
            throw DomainException.Forbidden("You do not have permission to modify this project.");
        }
    }

    private async Task<bool> CanAccessAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (CanManage(project, caller))
        {
            return true;
        }

        // Otherwise the caller needs an explicit share row.
        return await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken) is not null;
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static ProjectDto ToDto(Project p) =>
        new(p.Id, p.OwnerId, p.OwnerKind, p.Kind, p.Name, p.Description, p.Status, p.CreatedAt, p.UpdatedAt);

    private static ProjectTrashItemDto ToTrashDto(Project p)
    {
        var deletedAt = p.DeletedAt ?? DateTimeOffset.UtcNow;
        var remaining = (deletedAt + TrashRetention) - DateTimeOffset.UtcNow;
        var purgesInDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        return new ProjectTrashItemDto(p.Id, p.Kind, p.Name, p.Description, deletedAt, purgesInDays);
    }
}
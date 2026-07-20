using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>Implements the public <see cref="IProjectsApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class ProjectsApi(IProjectRepository projects, IAuthApi auth) : IProjectsApi
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        projects.ExistsAsync(projectId, cancellationToken);

    public async Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        return project is null
            ? null
            : new ProjectSummary(project.Id, project.OwnerId, ToContractOwnerKind(project.OwnerKind), ToContractKind(project.Kind), project.Name, project.Status);
    }

    public async Task<ProjectDocumentAccess> RequireReadAccessAsync(
        Guid projectId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(projectId, cancellationToken);
        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        return ToDocumentAccess(project);
    }

    public async Task<ProjectDocumentAccess> RequireWriteAccessAsync(
        Guid projectId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(projectId, cancellationToken);
        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
        if (!await CanWriteAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have permission to modify this project.");
        }

        return ToDocumentAccess(project);
    }

    public async Task<int> CountByWorkspaceAsync(
        Guid ownerId,
        ProjectOwnerKind ownerKind,
        CancellationToken cancellationToken = default)
    {
        var (_, total) = await projects.ListByWorkspaceAsync(ToModelOwnerKind(ownerKind), ownerId, 1, 1, cancellationToken);
        return total;
    }

    private static ProjectOwnerKind ToContractOwnerKind(OwnerKind ownerKind) =>
        ownerKind == OwnerKind.Studio ? ProjectOwnerKind.Studio : ProjectOwnerKind.User;

    private static OwnerKind ToModelOwnerKind(ProjectOwnerKind ownerKind) =>
        ownerKind == ProjectOwnerKind.Studio ? OwnerKind.Studio : OwnerKind.User;

    private static ProjectContentKind ToContractKind(ProjectKind kind) =>
        kind == ProjectKind.Image ? ProjectContentKind.Image : ProjectContentKind.Video;

    private async Task<Project> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        return project.DeletedAt is not null
            ? throw DomainException.NotFound("Project not found.")
            : project;
    }

    private async Task EnsurePersistedStudioMembershipAsync(
        Project project,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        if (project.OwnerKind == OwnerKind.Studio
            && await auth.GetStudioMemberAsync(project.OwnerId, caller.UserId, cancellationToken) is null)
        {
            throw DomainException.Forbidden("You are not a member of this studio.");
        }
    }

    private async Task<bool> CanWriteAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (project.OwnerKind == OwnerKind.User)
        {
            if (caller.OwnsAsUser(project.OwnerId))
            {
                return true;
            }

            var share = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
            return share is { IsHidden: false, Role: ProjectRole.Owner or ProjectRole.Editor };
        }

        if (caller.IsStudioOwner(project.OwnerId))
        {
            return true;
        }

        var access = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
        if (access is { IsHidden: true } or { Role: ProjectRole.Viewer })
        {
            return false;
        }

        if (access is { Role: ProjectRole.Owner or ProjectRole.Editor })
        {
            return true;
        }

        return caller.CanWriteStudioContent(project.OwnerId);
    }

    private async Task<bool> CanAccessAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (project.OwnerKind == OwnerKind.Studio)
        {
            if (caller.IsStudioOwner(project.OwnerId))
            {
                return true;
            }

            if (!caller.InStudio(project.OwnerId))
            {
                return false;
            }

            var studioOverride = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
            return studioOverride?.IsHidden != true;
        }

        if (project.OwnerKind == OwnerKind.User && caller.OwnsAsUser(project.OwnerId))
        {
            return true;
        }

        return await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken) is { IsHidden: false };
    }

    private static ProjectDocumentAccess ToDocumentAccess(Project project) =>
        new(project.Id, ToContractKind(project.Kind), project.Name, project.UpdatedAt);
}

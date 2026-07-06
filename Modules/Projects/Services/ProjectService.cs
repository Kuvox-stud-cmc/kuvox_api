using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using System.Text.Json;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Real Projects business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Persists via
/// <see cref="IProjectRepository"/>, resolves invitees through the Auth public contract
/// (<see cref="IAuthApi"/>, Rule 2).
/// </summary>
internal sealed class ProjectService(IProjectRepository projects, IAuthApi auth, INotificationsApi notifications)
    : IProjectService
{
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    public async Task<PagedResult<ProjectDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
        {
            items = await FilterVisibleAsync(items, caller, cancellationToken);
            total = items.Count;
        }
        var flags = await projects.GetStarFlagsAsync(items.Select(project => project.Id), caller.UserId, cancellationToken);
        var mediaCounts = await projects.GetMediaCountsAsync(items.Select(project => project.Id), cancellationToken);
        return new PagedResult<ProjectDto>(
            items.Select(project => ToDto(project, flags.GetValueOrDefault(project.Id), mediaCounts.GetValueOrDefault(project.Id))).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<PagedResult<ProjectDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListSharedWithUserAsync(userId, page, pageSize, cancellationToken);
        var flags = await projects.GetStarFlagsAsync(items.Select(item => item.Project.Id), userId, cancellationToken);
        var mediaCounts = await projects.GetMediaCountsAsync(items.Select(item => item.Project.Id), cancellationToken);
        var owners = await GetUserOwnerSummariesAsync(items.Select(item => item.Project), cancellationToken);
        return new PagedResult<ProjectDto>(
            items.Select(item => ToDto(
                item.Project,
                flags.GetValueOrDefault(item.Project.Id),
                mediaCounts.GetValueOrDefault(item.Project.Id),
                owners.GetValueOrDefault(item.Project.OwnerId))).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<PagedResult<ProjectTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await projects.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
        {
            items = await FilterVisibleAsync(items, caller, cancellationToken);
            total = items.Count;
        }
        return new PagedResult<ProjectTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
    }

    public async Task<ProjectDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        var projectUser = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
        var mediaCount = await GetMediaCountAsync(project.Id, cancellationToken);
        return ToDto(project, projectUser?.IsStarred ?? false, mediaCount);
    }

    public async Task<ImageCompositionDto> GetImageCompositionAsync(
        Guid id,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireImageProjectReadAsync(project, caller, cancellationToken);

        var composition = await projects.GetImageCompositionAsync(project.Id, cancellationToken);
        return composition is null
            ? new ImageCompositionDto(project.Id, null, 0, null, null)
            : ToImageCompositionDto(composition);
    }

    public async Task<ImageCompositionDto> SaveImageCompositionAsync(
        Guid id,
        CallerContext caller,
        SaveImageCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireImageProjectWriteAsync(project, caller, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var documentJson = request.DocumentJson.GetRawText();
        var operationsJson = request.OperationsJson?.GetRawText() ?? "[]";
        var composition = await projects.GetImageCompositionAsync(project.Id, cancellationToken);
        var latestRevision = composition?.RevisionNumber ?? 0;
        if (request.BaseRevisionNumber != latestRevision)
        {
            throw DomainException.Conflict("The image composition changed on the server.");
        }

        if (composition is null)
        {
            composition = new ImageComposition
            {
                ProjectId = project.Id,
                DocumentJson = documentJson,
                RevisionNumber = 1,
                UpdatedByUserId = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await projects.AddImageCompositionAsync(composition, cancellationToken);
        }
        else
        {
            composition.DocumentJson = documentJson;
            composition.RevisionNumber += 1;
            composition.UpdatedByUserId = caller.UserId;
            composition.UpdatedAt = now;
        }

        await projects.AddImageCompositionRevisionAsync(
            new ImageCompositionRevision
            {
                ImageCompositionId = composition.Id,
                ProjectId = project.Id,
                RevisionNumber = composition.RevisionNumber,
                DocumentJson = documentJson,
                OperationsJson = operationsJson,
                CreatedByUserId = caller.UserId,
                CreatedAt = now,
            },
            cancellationToken);

        project.UpdatedAt = now;
        await projects.SaveChangesAsync(cancellationToken);
        return ToImageCompositionDto(composition);
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

        if (scope.IsStudio && !caller.CanWriteStudioContent(scope.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to create Studio projects.");
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
        await RequireWriteAsync(project, caller, cancellationToken);

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.Status = request.Status;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);

        var projectUser = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
        var mediaCount = await GetMediaCountAsync(project.Id, cancellationToken);
        return ToDto(project, projectUser?.IsStarred ?? false, mediaCount);
    }

    public async Task<ProjectDto> SetStarAsync(
        Guid id,
        CallerContext caller,
        ToggleProjectStarRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        var projectUser = await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken);
        if (projectUser is null)
        {
            if (request.IsStarred && project.OwnerKind == OwnerKind.User && caller.OwnsAsUser(project.OwnerId))
            {
                projectUser = CreateProjectUser(project.Id, caller.UserId, ProjectRole.Owner);
                projectUser.IsStarred = true;
                await projects.AddProjectUserAsync(projectUser, cancellationToken);
                await projects.SaveChangesAsync(cancellationToken);
            }

            var mediaCount = await GetMediaCountAsync(project.Id, cancellationToken);
            return ToDto(project, projectUser?.IsStarred ?? false, mediaCount);
        }

        if (projectUser.IsStarred != request.IsStarred)
        {
            projectUser.IsStarred = request.IsStarred;
            projectUser.UpdatedAt = DateTimeOffset.UtcNow;
            await projects.SaveChangesAsync(cancellationToken);
        }

        var updatedMediaCount = await GetMediaCountAsync(project.Id, cancellationToken);
        return ToDto(project, projectUser.IsStarred, updatedMediaCount);
    }

    public async Task ShareAsync(
        Guid id, CallerContext caller, ShareProjectRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Role == ProjectRole.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(project, caller, cancellationToken);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (project.OwnerKind == OwnerKind.User && invitee.Id == project.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await projects.GetProjectUserAsync(project.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            await projects.AddProjectUserAsync(CreateProjectUser(project.Id, invitee.Id, request.Role), cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.IsHidden = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await projects.SaveChangesAsync(cancellationToken);
        await notifications.CreateAsync(
            invitee.Id,
            null,
            "ProjectAccessChanged",
            $"A project was shared with you: {project.Name}.",
            EditorPath(project),
            cancellationToken);
    }

    public async Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(project, caller, cancellationToken);

        var share = await projects.GetProjectUserAsync(project.Id, userId, cancellationToken);
        if (share is not null)
        {
            projects.RemoveProjectUser(share);
            await projects.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProjectAccessMemberDto>> ListAccessAsync(
        Guid id,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireStudioAccessManage(project, caller);
        return await BuildAccessRowsAsync(project, caller, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectAccessMemberDto>> UpdateAccessAsync(
        Guid id,
        CallerContext caller,
        UpdateProjectAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        RequireStudioAccessManage(project, caller);
        var target = await auth.GetStudioMemberAsync(project.OwnerId, request.UserId, cancellationToken)
            ?? throw DomainException.NotFound("Studio member not found.");

        RequireCanManageTarget(caller, project.OwnerId, target.Role);
        var role = request.Role ?? DefaultProjectRoleForStudioRole(target.Role);
        if (role == ProjectRole.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var access = await projects.GetProjectUserAsync(project.Id, request.UserId, cancellationToken);
        if (access is null)
        {
            access = CreateProjectUser(project.Id, request.UserId, role);
            access.IsHidden = request.IsHidden;
            await projects.AddProjectUserAsync(access, cancellationToken);
        }
        else
        {
            access.Role = role;
            access.IsHidden = request.IsHidden;
            access.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await projects.SaveChangesAsync(cancellationToken);
        return await BuildAccessRowsAsync(project, caller, cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(project, caller, cancellationToken);

        project.DeletedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        await RequireTrashManageAsync(project, caller, cancellationToken);

        project.DeletedAt = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        await RequireTrashManageAsync(project, caller, cancellationToken);

        projects.Remove(project);
        await projects.SaveChangesAsync(cancellationToken);
    }

    private async Task<Project> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken);
        return project is null || project.DeletedAt is not null
            ? throw DomainException.NotFound("Project not found.")
            : project;
    }

    private static bool CanRead(Project project, CallerContext caller) =>
        project.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(project.OwnerId)
            : caller.InStudio(project.OwnerId);

    private static bool CanWrite(Project project, CallerContext caller) =>
        project.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(project.OwnerId)
            : caller.CanWriteStudioContent(project.OwnerId);

    private static bool CanManageTrash(Project project, CallerContext caller) =>
        project.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(project.OwnerId)
            : caller.CanManageStudioAccess(project.OwnerId);

    private static void RequireWrite(Project project, CallerContext caller)
    {
        if (!CanWrite(project, caller))
        {
            throw DomainException.Forbidden("You do not have permission to modify this project.");
        }
    }

    private static void RequireTrashManage(Project project, CallerContext caller)
    {
        if (!CanManageTrash(project, caller))
        {
            throw DomainException.Forbidden("You do not have permission to manage Studio trash.");
        }
    }

    private async Task RequireWriteAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!await CanWriteAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have permission to modify this project.");
        }
    }

    private async Task RequireImageProjectReadAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (project.Kind != ProjectKind.Image)
        {
            throw DomainException.BadRequest("Image compositions are only available for image projects.");
        }

        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }
    }

    private async Task RequireImageProjectWriteAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (project.Kind != ProjectKind.Image)
        {
            throw DomainException.BadRequest("Image compositions are only available for image projects.");
        }

        await RequireWriteAsync(project, caller, cancellationToken);
    }

    private async Task RequireTrashManageAsync(Project project, CallerContext caller, CancellationToken cancellationToken)
    {
        if (project.OwnerKind == OwnerKind.Studio && !await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        if (!CanManageTrash(project, caller))
        {
            throw DomainException.Forbidden("You do not have permission to manage Studio trash.");
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

        if (CanRead(project, caller))
        {
            return true;
        }

        // Otherwise the caller needs an explicit share row.
        return await projects.GetProjectUserAsync(project.Id, caller.UserId, cancellationToken) is { IsHidden: false };
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static ProjectUser CreateProjectUser(Guid projectId, Guid userId, ProjectRole role) =>
        new() { ProjectId = projectId, UserId = userId, Role = role, IsStarred = false, IsTemplate = false, IsHidden = false };

    private static string EditorPath(Project project) =>
        project.Kind == ProjectKind.Image
            ? $"/editor/image/{project.Id}"
            : $"/editor/video/{project.Id}";

    private async Task<IReadOnlyList<Project>> FilterVisibleAsync(
        IReadOnlyList<Project> items,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var visible = new List<Project>();
        foreach (var item in items)
        {
            if (await CanAccessAsync(item, caller, cancellationToken))
            {
                visible.Add(item);
            }
        }

        return visible;
    }

    private async Task<IReadOnlyList<ProjectAccessMemberDto>> BuildAccessRowsAsync(
        Project project,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(project.OwnerId, cancellationToken);
        var rows = new List<ProjectAccessMemberDto>();
        foreach (var member in members)
        {
            var access = await projects.GetProjectUserAsync(project.Id, member.UserId, cancellationToken);
            rows.Add(new ProjectAccessMemberDto(
                member.UserId,
                member.Email,
                member.DisplayName,
                member.Role,
                access?.Role ?? DefaultProjectRoleForStudioRole(member.Role),
                access?.Role,
                access?.IsHidden ?? false,
                CanManageTarget(caller, project.OwnerId, member.Role)));
        }

        return rows;
    }

    private static void RequireStudioAccessManage(Project project, CallerContext caller)
    {
        if (project.OwnerKind != OwnerKind.Studio)
        {
            throw DomainException.BadRequest("Item access overrides are only available for Studio projects.");
        }

        if (!caller.CanManageStudioAccess(project.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to manage item access.");
        }
    }

    private static void RequireCanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (!CanManageTarget(caller, studioId, targetRole))
        {
            throw DomainException.Forbidden("You cannot restrict a member with that Studio role.");
        }
    }

    private static bool CanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (caller.IsStudioOwner(studioId))
        {
            return !string.Equals(targetRole, "Owner", StringComparison.Ordinal);
        }

        return caller.IsStudioAdmin(studioId)
            && !string.Equals(targetRole, "Owner", StringComparison.Ordinal)
            && !string.Equals(targetRole, "Admin", StringComparison.Ordinal);
    }

    private static ProjectRole DefaultProjectRoleForStudioRole(string studioRole) =>
        string.Equals(studioRole, "Viewer", StringComparison.Ordinal)
            ? ProjectRole.Viewer
            : ProjectRole.Editor;

    private async Task<int> GetMediaCountAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var counts = await projects.GetMediaCountsAsync([projectId], cancellationToken);
        return counts.GetValueOrDefault(projectId);
    }

    private async Task<IReadOnlyDictionary<Guid, UserSummary>> GetUserOwnerSummariesAsync(
        IEnumerable<Project> projectItems,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<Guid, UserSummary>();
        foreach (var ownerId in projectItems
            .Where(item => item.OwnerKind == OwnerKind.User)
            .Select(item => item.OwnerId)
            .Distinct())
        {
            var summary = await auth.GetSummaryAsync(ownerId, cancellationToken);
            if (summary is not null)
            {
                owners[ownerId] = summary;
            }
        }

        return owners;
    }

    private static ProjectDto ToDto(Project p, bool isStarred = false, int mediaCount = 0, UserSummary? owner = null) =>
        new(
            p.Id,
            p.OwnerId,
            p.OwnerKind,
            owner?.Email,
            owner?.DisplayName,
            p.Kind,
            p.Name,
            p.Description,
            p.DurationSeconds,
            p.Status,
            p.CreatedAt,
            p.UpdatedAt,
            mediaCount,
            isStarred);

    private static ProjectTrashItemDto ToTrashDto(Project p)
    {
        var deletedAt = p.DeletedAt ?? DateTimeOffset.UtcNow;
        var remaining = (deletedAt + TrashRetention) - DateTimeOffset.UtcNow;
        var purgesInDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        return new ProjectTrashItemDto(p.Id, p.Kind, p.Name, p.Description, deletedAt, purgesInDays);
    }

    private static ImageCompositionDto ToImageCompositionDto(ImageComposition composition) =>
        new(
            composition.ProjectId,
            JsonSerializer.Deserialize<JsonElement>(composition.DocumentJson),
            composition.RevisionNumber,
            composition.UpdatedAt,
            composition.UpdatedByUserId);
}

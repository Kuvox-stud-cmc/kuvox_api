using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Timelines.Contracts;
using MediatR;
using Microsoft.Extensions.Options;
using System.Text.Json;
using MediaKind = Kuvox.Api.Modules.Media.Enums.MediaKind;
using MediaOwnerKind = Kuvox.Api.Modules.Media.Enums.OwnerKind;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Real Projects business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Persists via
/// <see cref="IProjectRepository"/>, resolves invitees through the Auth public contract
/// (<see cref="IAuthApi"/>, Rule 2).
/// </summary>
internal sealed class ProjectService(
    IProjectRepository projects,
    IAuthApi auth,
    INotificationsApi notifications,
    IMediaApi media,
    BusinessCache cache,
    CacheGenerationManager generations,
    CacheKeyFactory cacheKeys,
    IOptions<CachingOptions> cachingOptions,
    EditorDocumentCache documentCache,
    ITimelinesApi timelines,
    IMediator mediator)
    : IProjectService
{
    private readonly ProjectCacheOptions _cacheOptions = cachingOptions.Value.Projects;
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    public async Task<PagedResult<ProjectDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        await RequireWorkspaceAccessAsync(scope, caller, cancellationToken);
        async Task<PagedResult<ProjectDto>> Load(CancellationToken ct)
        {
            var (items, total) = await projects.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, ct);
            if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
            {
                items = await FilterVisibleAsync(items, caller, ct);
                total = items.Count;
            }
            var flags = await projects.GetStarFlagsAsync(items.Select(project => project.Id), caller.UserId, ct);
            var mediaCounts = await projects.GetMediaCountsAsync(items.Select(project => project.Id), ct);
            return new PagedResult<ProjectDto>(
                items.Select(project => ToDto(project, flags.GetValueOrDefault(project.Id), mediaCounts.GetValueOrDefault(project.Id))).ToList(),
                page, pageSize, total);
        }
        return await GetProjectListCachedAsync(scope, caller.UserId, "workspace", page, pageSize, Load, cancellationToken);
    }

    public async Task<PagedResult<ProjectDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        async Task<PagedResult<ProjectDto>> Load(CancellationToken ct)
        {
            var (items, total) = await projects.ListSharedWithUserAsync(userId, page, pageSize, ct);
            var flags = await projects.GetStarFlagsAsync(items.Select(item => item.Project.Id), userId, ct);
            var mediaCounts = await projects.GetMediaCountsAsync(items.Select(item => item.Project.Id), ct);
            var owners = await GetUserOwnerSummariesAsync(items.Select(item => item.Project), ct);
            return new PagedResult<ProjectDto>(
                items.Select(item => ToDto(item.Project, flags.GetValueOrDefault(item.Project.Id),
                    mediaCounts.GetValueOrDefault(item.Project.Id), owners.GetValueOrDefault(item.Project.OwnerId))).ToList(),
                page, pageSize, total);
        }
        return await GetSharedProjectsCachedAsync(userId, page, pageSize, Load, cancellationToken);
    }

    public async Task<PagedResult<ProjectTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        await RequireWorkspaceAccessAsync(scope, caller, cancellationToken);
        async Task<PagedResult<ProjectTrashItemDto>> Load(CancellationToken ct)
        {
            var (items, total) = await projects.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, ct);
            if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
            {
                items = await FilterVisibleAsync(items, caller, ct);
                total = items.Count;
            }
            return new PagedResult<ProjectTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
        }
        return await GetProjectListCachedAsync(scope, caller.UserId, "trash", page, pageSize, Load, cancellationToken);
    }

    public async Task<ProjectDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        return await GetAuthorizedProjectAsync(project, caller, cancellationToken);
    }

    private async Task<ProjectDto> GetAuthorizedProjectAsync(
        Project project,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var generation = await GetProjectGenerationAsync(project.Id, cancellationToken);
        var mediaGeneration = cache.IsEnabled(_cacheOptions)
            ? await generations.GetAsync("media-projection", "global", cancellationToken)
            : null;
        async Task<ProjectDto> Load(CancellationToken ct)
        {
            var projectUser = await projects.GetProjectUserAsync(project.Id, caller.UserId, ct);
            var mediaCount = await GetMediaCountAsync(project.Id, ct);
            return ToDto(project, projectUser?.IsStarred ?? false, mediaCount);
        }
        if (generation is null || mediaGeneration is null) return await Load(cancellationToken);
        var key = BusinessCacheKey.Create(cacheKeys, "project-detail", "project", project.Id, "viewer", caller.UserId,
            "gen", generation, "media-gen", mediaGeneration);
        return await cache.GetOrCreateAsync("projects", "detail", _cacheOptions, key,
            TimeSpan.FromSeconds(_cacheOptions.DetailTtlSeconds), Load, cancellationToken);
    }

    public async Task<PagedResult<ProjectMediaDto>> ListMediaAsync(
        Guid id,
        CallerContext caller,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var project = await LoadLiveAsync(id, cancellationToken);
        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        return await ListAuthorizedMediaAsync(project, caller, page, pageSize, cancellationToken);
    }

    private async Task<PagedResult<ProjectMediaDto>> ListAuthorizedMediaAsync(
        Project project,
        CallerContext caller,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        async Task<PagedResult<ProjectMediaDto>> Load(CancellationToken ct)
        {
            var (rows, total) = await projects.ListProjectMediaAsync(project.Id, page, pageSize, ct);
            var resolved = await media.ResolveAsync(rows.Select(row => row.MediaId).ToArray(), caller, ct);
            var resolvedById = resolved.ToDictionary(item => item.MediaId);
            var items = rows.Select(row =>
            {
                var resolution = resolvedById.GetValueOrDefault(row.MediaId)
                    ?? new MediaResolution(row.MediaId, row.Kind, MediaResolutionAvailability.Missing, null);
                return ToProjectMediaDto(resolution, row.Kind);
            }).ToList();
            return new PagedResult<ProjectMediaDto>(items, page, pageSize, total);
        }
        var generation = await GetProjectGenerationAsync(project.Id, cancellationToken);
        var mediaGeneration = cache.IsEnabled(_cacheOptions)
            ? await generations.GetAsync("media-projection", "global", cancellationToken)
            : null;
        if (generation is null || mediaGeneration is null) return await Load(cancellationToken);
        var key = BusinessCacheKey.Create(cacheKeys, "project-media", "project", project.Id, "viewer", caller.UserId,
            "page", page, "size", pageSize, "gen", generation, "media-gen", mediaGeneration);
        return await cache.GetOrCreateAsync("projects", "media", _cacheOptions, key,
            TimeSpan.FromSeconds(_cacheOptions.MediaTtlSeconds), Load, cancellationToken,
            result => result.Items.All(item => item.Status is null
                || string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Status, "Failed", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<ProjectMediaDto>> AttachMediaAsync(
        Guid id,
        CallerContext caller,
        AttachProjectMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        var mediaIds = request.MediaIds
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (mediaIds.Length == 0)
        {
            throw DomainException.BadRequest("Choose at least one media item.");
        }

        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(project, caller, cancellationToken);

        var resolved = await media.ResolveAsync(mediaIds, caller, cancellationToken);
        var resolvedById = resolved.ToDictionary(item => item.MediaId);
        foreach (var mediaId in mediaIds)
        {
            if (!resolvedById.TryGetValue(mediaId, out var resolution) || resolution.Availability == MediaResolutionAvailability.Missing)
            {
                throw DomainException.NotFound($"Media {mediaId} was not found.");
            }

            if (resolution.Availability == MediaResolutionAvailability.Deleted)
            {
                throw DomainException.BadRequest($"Media {mediaId} is in Trash and cannot be attached to this project.");
            }

            if (resolution.Availability == MediaResolutionAvailability.Inaccessible)
            {
                throw DomainException.Forbidden($"You do not have access to media {mediaId}.");
            }

            var summary = resolution.Media
                ?? throw DomainException.BadRequest($"Media {mediaId} cannot be attached to this project.");
            if (!IsSameWorkspace(project, summary))
            {
                throw DomainException.BadRequest($"Media {mediaId} belongs to a different workspace.");
            }
        }

        foreach (var resolution in resolved)
        {
            if (resolution.Media is null || resolution.Kind is not { } kind)
            {
                continue;
            }

            await projects.AddProjectMediaAsync(project.Id, resolution.MediaId, kind, cancellationToken);
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
        await InvalidateProjectAsync(project);

        return resolved.Select(item => ToProjectMediaDto(item, item.Kind ?? MediaKind.Video)).ToList();
    }

    public async Task<ImageCompositionDto> GetImageCompositionAsync(
        Guid id,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireImageProjectReadAsync(project, caller, cancellationToken);

        return await GetAuthorizedImageCompositionAsync(project, cancellationToken);
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
        await InvalidateProjectAsync(project);
        var result = ToImageCompositionDto(composition);
        await documentCache.WriteImageCompositionAsync(project.Id, composition.RevisionNumber, result);
        return result;
    }

    public async Task<ProjectEditorBootstrapDto> GetEditorBootstrapAsync(
        Guid id,
        CallerContext caller,
        int mediaPage,
        int mediaPageSize,
        CancellationToken cancellationToken = default)
    {
        (mediaPage, mediaPageSize) = Normalize(mediaPage, mediaPageSize);
        var project = await LoadLiveAsync(id, cancellationToken);
        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
        if (!await CanAccessAsync(project, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this project.");
        }

        var projectDto = await GetAuthorizedProjectAsync(project, caller, cancellationToken);
        var projectMedia = await ListAuthorizedMediaAsync(project, caller, mediaPage, mediaPageSize, cancellationToken);
        if (project.Kind == ProjectKind.Video)
        {
            var snapshot = await timelines.GetAuthorizedProjectSnapshotAsync(ToDocumentAccess(project), cancellationToken);
            return new ProjectEditorBootstrapDto(
                projectDto,
                projectMedia,
                snapshot is null ? null : new EditorBootstrapTimelineDto(
                    snapshot.ProjectId,
                    snapshot.TimelineId,
                    snapshot.RevisionId,
                    snapshot.DocumentJson,
                    snapshot.RevisionNumber,
                    snapshot.DocumentSchemaVersion,
                    snapshot.Source,
                    snapshot.Label,
                    snapshot.UpdatedAt,
                    snapshot.UpdatedByUserId),
                null);
        }

        var composition = await GetAuthorizedImageCompositionAsync(project, cancellationToken);
        return new ProjectEditorBootstrapDto(
            projectDto,
            projectMedia,
            null,
            composition);
    }

    private async Task<ImageCompositionDto> GetAuthorizedImageCompositionAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        if (!documentCache.DocumentsEnabled)
        {
            var current = await projects.GetImageCompositionAsync(project.Id, cancellationToken);
            return current is null
                ? new ImageCompositionDto(project.Id, null, 0, null, null)
                : ToImageCompositionDto(current);
        }

        var identity = await projects.GetImageCompositionIdentityAsync(project.Id, cancellationToken);
        if (identity is null || identity.RevisionNumber == 0)
        {
            return new ImageCompositionDto(project.Id, null, 0, null, null);
        }

        return await documentCache.GetImageCompositionAsync(
            project.Id,
            identity.RevisionNumber,
            async ct =>
            {
                var revision = await projects.GetImageCompositionRevisionAsync(project.Id, identity.RevisionNumber, ct);
                if (revision is not null)
                {
                    return ToImageCompositionDto(revision);
                }

                var current = await projects.GetImageCompositionAsync(project.Id, ct)
                    ?? throw DomainException.NotFound("Image composition not found.");
                return ToImageCompositionDto(current);
            },
            cancellationToken);
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
        await InvalidateProjectAsync(project);

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
        await InvalidateProjectAsync(project, summaryChanged: true);

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
                await InvalidateProjectAsync(project);
            }

            var mediaCount = await GetMediaCountAsync(project.Id, cancellationToken);
            return ToDto(project, projectUser?.IsStarred ?? false, mediaCount);
        }

        if (projectUser.IsStarred != request.IsStarred)
        {
            projectUser.IsStarred = request.IsStarred;
            projectUser.UpdatedAt = DateTimeOffset.UtcNow;
            await projects.SaveChangesAsync(cancellationToken);
            await InvalidateProjectAsync(project);
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
        await InvalidateProjectAsync(project);
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
            await InvalidateProjectAsync(project);
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
        await InvalidateProjectAsync(project);
        return await BuildAccessRowsAsync(project, caller, cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(project, caller, cancellationToken);

        project.DeletedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
        await InvalidateProjectAsync(project, summaryChanged: true);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        await RequireTrashManageAsync(project, caller, cancellationToken);

        project.DeletedAt = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await projects.SaveChangesAsync(cancellationToken);
        await InvalidateProjectAsync(project, summaryChanged: true);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Project not found.");
        await RequireTrashManageAsync(project, caller, cancellationToken);

        projects.Remove(project);
        await projects.SaveChangesAsync(cancellationToken);
        await InvalidateProjectAsync(project, summaryChanged: true);
    }

    private async Task<Project> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken);
        return project is null || project.DeletedAt is not null
            ? throw DomainException.NotFound("Project not found.")
            : project;
    }

    private async Task RequireWorkspaceAccessAsync(
        WorkspaceScope scope,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        if (!scope.IsStudio)
        {
            if (!caller.OwnsAsUser(scope.OwnerId))
            {
                throw DomainException.Forbidden("You do not have access to this workspace.");
            }
            return;
        }

        if (await auth.GetStudioMemberAsync(scope.OwnerId, caller.UserId, cancellationToken) is null)
        {
            throw DomainException.Forbidden("You are not a member of this studio.");
        }
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

    private Task<string?> GetProjectGenerationAsync(Guid projectId, CancellationToken cancellationToken) =>
        cache.IsEnabled(_cacheOptions)
            ? generations.GetAsync("projects", $"project-{projectId:N}", cancellationToken)
            : Task.FromResult<string?>(null);

    private async Task<PagedResult<T>> GetProjectListCachedAsync<T>(
        WorkspaceScope scope,
        Guid viewerId,
        string kind,
        int page,
        int pageSize,
        Func<CancellationToken, Task<PagedResult<T>>> factory,
        CancellationToken cancellationToken)
    {
        if (!cache.IsEnabled(_cacheOptions)) return await factory(cancellationToken);
        var generation = await generations.GetAsync(
            "projects", $"owner-{OwnerKindOf(scope)}-{scope.OwnerId:N}", cancellationToken);
        var mediaGeneration = await generations.GetAsync("media-projection", "global", cancellationToken);
        if (generation is null || mediaGeneration is null) return await factory(cancellationToken);
        var key = BusinessCacheKey.Create(
            cacheKeys, "project-list", "owner", OwnerKindOf(scope), scope.OwnerId,
            "viewer", viewerId, "kind", kind, "page", page, "size", pageSize,
            "filter", BusinessCacheKey.Hash("sort-updated-desc"), "gen", generation, "media-gen", mediaGeneration);
        return await cache.GetOrCreateAsync(
            "projects", kind, _cacheOptions, key, TimeSpan.FromSeconds(_cacheOptions.ListTtlSeconds), factory, cancellationToken);
    }

    private async Task<PagedResult<ProjectDto>> GetSharedProjectsCachedAsync(
        Guid viewerId,
        int page,
        int pageSize,
        Func<CancellationToken, Task<PagedResult<ProjectDto>>> factory,
        CancellationToken cancellationToken)
    {
        if (!cache.IsEnabled(_cacheOptions)) return await factory(cancellationToken);
        var generation = await generations.GetAsync("projects", "shared-global", cancellationToken);
        var mediaGeneration = await generations.GetAsync("media-projection", "global", cancellationToken);
        if (generation is null || mediaGeneration is null) return await factory(cancellationToken);
        var key = BusinessCacheKey.Create(
            cacheKeys, "project-list", "owner", "shared", viewerId, "viewer", viewerId,
            "kind", "shared", "page", page, "size", pageSize,
            "filter", BusinessCacheKey.Hash("sort-updated-desc"), "gen", generation, "media-gen", mediaGeneration);
        return await cache.GetOrCreateAsync(
            "projects", "shared", _cacheOptions, key, TimeSpan.FromSeconds(_cacheOptions.ListTtlSeconds), factory, cancellationToken);
    }

    private async Task InvalidateProjectAsync(Project project, bool summaryChanged = false)
    {
        if (cache.IsEnabled(_cacheOptions))
        {
            _ = await generations.BumpAsync("projects", $"project-{project.Id:N}");
            _ = await generations.BumpAsync("projects", $"owner-{project.OwnerKind}-{project.OwnerId:N}");
            _ = await generations.BumpAsync("projects", "shared-global");
        }
        if (project.OwnerKind == OwnerKind.Studio && cache.IsEnabled(cachingOptions.Value.StorageUsage))
        {
            _ = await generations.BumpAsync("storage-usage", $"owner-Studio-{project.OwnerId:N}");
        }

        if (summaryChanged && project.OwnerKind == OwnerKind.Studio)
        {
            await mediator.Publish(new ProjectSummaryChangedEvent(project.OwnerId));
        }
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

        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);

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

        await EnsurePersistedStudioMembershipAsync(project, caller, cancellationToken);
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

    private static bool IsSameWorkspace(Project project, MediaSummary summary) =>
        project.OwnerId == summary.OwnerId
        && ((project.OwnerKind == OwnerKind.User && summary.OwnerKind == MediaOwnerKind.User)
            || (project.OwnerKind == OwnerKind.Studio && summary.OwnerKind == MediaOwnerKind.Studio));

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

    private static ProjectMediaDto ToProjectMediaDto(MediaResolution resolution, MediaKind fallbackKind)
    {
        var summary = resolution.Media;
        return new ProjectMediaDto(
            resolution.MediaId,
            resolution.Kind ?? fallbackKind,
            AvailabilityName(resolution.Availability),
            summary?.Filename,
            summary?.OwnerId,
            summary?.OwnerKind,
            summary?.Status,
            IsLiveAccessible(resolution) ? summary?.StorageKey : null,
            IsLiveAccessible(resolution) ? summary?.SizeBytes : null,
            IsLiveAccessible(resolution) ? summary?.CanonicalStorageKey : null,
            IsLiveAccessible(resolution) ? summary?.ProxyStorageKey : null,
            IsLiveAccessible(resolution) ? summary?.ThumbnailStorageKey : null,
            IsLiveAccessible(resolution) ? summary?.ErrorMessage : null,
            IsLiveAccessible(resolution) ? summary?.DurationSeconds : null,
            IsLiveAccessible(resolution) ? summary?.Width : null,
            IsLiveAccessible(resolution) ? summary?.Height : null,
            IsLiveAccessible(resolution) ? summary?.Codec : null,
            IsLiveAccessible(resolution) ? summary?.FrameRate : null,
            IsLiveAccessible(resolution) ? summary?.CreatedAt : null,
            IsLiveAccessible(resolution) ? summary?.SearchRevision : null);
    }

    private static bool IsLiveAccessible(MediaResolution resolution) =>
        resolution.Availability is MediaResolutionAvailability.Available
            or MediaResolutionAvailability.Processing
            or MediaResolutionAvailability.Failed;

    private static string AvailabilityName(MediaResolutionAvailability availability) =>
        availability switch
        {
            MediaResolutionAvailability.Available => "available",
            MediaResolutionAvailability.Processing => "processing",
            MediaResolutionAvailability.Failed => "failed",
            MediaResolutionAvailability.Deleted => "deleted",
            MediaResolutionAvailability.Inaccessible => "inaccessible",
            _ => "missing",
        };

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

    private static ImageCompositionDto ToImageCompositionDto(ImageCompositionRevision revision) =>
        new(
            revision.ProjectId,
            JsonSerializer.Deserialize<JsonElement>(revision.DocumentJson),
            revision.RevisionNumber,
            revision.CreatedAt,
            revision.CreatedByUserId);

    private static ProjectDocumentAccess ToDocumentAccess(Project project) =>
        new(
            project.Id,
            project.Kind == ProjectKind.Image ? ProjectContentKind.Image : ProjectContentKind.Video,
            project.Name,
            project.UpdatedAt);
}

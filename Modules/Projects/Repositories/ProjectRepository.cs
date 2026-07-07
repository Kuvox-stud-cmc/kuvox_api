using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Microsoft.EntityFrameworkCore;
using MediaKind = Kuvox.Api.Modules.Media.Enums.MediaKind;

namespace Kuvox.Api.Modules.Projects.Repositories;

internal sealed class ProjectRepository(ProjectsDbContext db) : IProjectRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Project> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Projects
            .Where(p => p.OwnerKind == ownerKind && p.OwnerId == ownerId && p.DeletedAt == null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<(Project Project, ProjectRole Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // "Shared with me" = junction rows for the caller, excluding anything they already own.
        var query =
            from pu in db.ProjectUsers
            join p in db.Projects on pu.ProjectId equals p.Id
            where pu.UserId == userId && !pu.IsHidden && p.DeletedAt == null && p.OwnerKind == OwnerKind.User && p.OwnerId != userId
            orderby p.UpdatedAt descending
            select new { Project = p, pu.Role };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(r => (r.Project, r.Role)).ToList(), total);
    }

    public async Task<(IReadOnlyList<Project> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Projects
            .Where(p => p.OwnerKind == ownerKind && p.OwnerId == ownerId && p.DeletedAt != null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Project>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await db.Projects.Where(p => p.DeletedAt != null && p.DeletedAt < cutoff).ToListAsync(cancellationToken);

    public Task<ProjectUser?> GetProjectUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default) =>
        db.ProjectUsers.FirstOrDefaultAsync(pu => pu.ProjectId == projectId && pu.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, bool>> GetStarFlagsAsync(
        IEnumerable<Guid> projectIds,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        return await db.ProjectUsers
            .Where(pu => pu.UserId == userId && ids.Contains(pu.ProjectId))
            .ToDictionaryAsync(pu => pu.ProjectId, pu => pu.IsStarred, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMediaCountsAsync(
        IEnumerable<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = new Dictionary<Guid, int>();
        await AddProjectMediaCountsAsync(
            counts,
            db.ProjectImages.Where(pm => ids.Contains(pm.ProjectId)),
            cancellationToken);
        await AddProjectMediaCountsAsync(
            counts,
            db.ProjectAudios.Where(pm => ids.Contains(pm.ProjectId)),
            cancellationToken);
        await AddProjectMediaCountsAsync(
            counts,
            db.ProjectVideos.Where(pm => ids.Contains(pm.ProjectId)),
            cancellationToken);

        return counts;
    }

    public async Task<(IReadOnlyList<ProjectMediaRow> Items, int Total)> ListProjectMediaAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var imageRows = db.ProjectImages
            .Where(pm => pm.ProjectId == projectId);
        var audioRows = db.ProjectAudios
            .Where(pm => pm.ProjectId == projectId);
        var videoRows = db.ProjectVideos
            .Where(pm => pm.ProjectId == projectId);

        var imageCount = await imageRows.CountAsync(cancellationToken);
        var audioCount = await audioRows.CountAsync(cancellationToken);
        var videoCount = await videoRows.CountAsync(cancellationToken);
        var total = imageCount + audioCount + videoCount;
        var take = page * pageSize;

        var images = await imageRows
            .OrderByDescending(pm => pm.CreatedAt)
            .Take(take)
            .Select(pm => new ProjectMediaRow(pm.ProjectId, pm.MediaId, MediaKind.Image, pm.CreatedAt))
            .ToListAsync(cancellationToken);
        var audios = await audioRows
            .OrderByDescending(pm => pm.CreatedAt)
            .Take(take)
            .Select(pm => new ProjectMediaRow(pm.ProjectId, pm.MediaId, MediaKind.Audio, pm.CreatedAt))
            .ToListAsync(cancellationToken);
        var videos = await videoRows
            .OrderByDescending(pm => pm.CreatedAt)
            .Take(take)
            .Select(pm => new ProjectMediaRow(pm.ProjectId, pm.MediaId, MediaKind.Video, pm.CreatedAt))
            .ToListAsync(cancellationToken);

        var items = images
            .Concat(audios)
            .Concat(videos)
            .OrderByDescending(row => row.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (items, total);
    }

    public async Task<IReadOnlySet<Guid>> GetAssociatedMediaIdsAsync(
        Guid projectId,
        IEnumerable<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        var ids = mediaIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var imageIds = await db.ProjectImages
            .Where(pm => pm.ProjectId == projectId && ids.Contains(pm.MediaId))
            .Select(pm => pm.MediaId)
            .ToListAsync(cancellationToken);
        var audioIds = await db.ProjectAudios
            .Where(pm => pm.ProjectId == projectId && ids.Contains(pm.MediaId))
            .Select(pm => pm.MediaId)
            .ToListAsync(cancellationToken);
        var videoIds = await db.ProjectVideos
            .Where(pm => pm.ProjectId == projectId && ids.Contains(pm.MediaId))
            .Select(pm => pm.MediaId)
            .ToListAsync(cancellationToken);

        return imageIds.Concat(audioIds).Concat(videoIds).ToHashSet();
    }

    public async Task AddProjectMediaAsync(
        Guid projectId,
        Guid mediaId,
        MediaKind kind,
        CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case MediaKind.Image:
                if (!await db.ProjectImages.AnyAsync(pm => pm.ProjectId == projectId && pm.MediaId == mediaId, cancellationToken))
                {
                    await db.ProjectImages.AddAsync(new ProjectImage { ProjectId = projectId, MediaId = mediaId }, cancellationToken);
                }
                break;
            case MediaKind.Audio:
                if (!await db.ProjectAudios.AnyAsync(pm => pm.ProjectId == projectId && pm.MediaId == mediaId, cancellationToken))
                {
                    await db.ProjectAudios.AddAsync(new ProjectAudio { ProjectId = projectId, MediaId = mediaId }, cancellationToken);
                }
                break;
            case MediaKind.Video:
                if (!await db.ProjectVideos.AnyAsync(pm => pm.ProjectId == projectId && pm.MediaId == mediaId, cancellationToken))
                {
                    await db.ProjectVideos.AddAsync(new ProjectVideo { ProjectId = projectId, MediaId = mediaId }, cancellationToken);
                }
                break;
            default:
                throw new NotSupportedException($"Unsupported media kind '{kind}'.");
        }
    }

    private static async Task AddProjectMediaCountsAsync<TProjectMedia>(
        Dictionary<Guid, int> counts,
        IQueryable<TProjectMedia> query,
        CancellationToken cancellationToken)
        where TProjectMedia : ProjectMedia
    {
        var rows = await query
            .GroupBy(pm => pm.ProjectId)
            .Select(group => new { ProjectId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            counts[row.ProjectId] = counts.GetValueOrDefault(row.ProjectId) + row.Count;
        }
    }

    public async Task<int> DeleteProjectMediaByMediaIdAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        deleted += await db.ProjectImages.Where(pm => pm.MediaId == mediaId).ExecuteDeleteAsync(cancellationToken);
        deleted += await db.ProjectAudios.Where(pm => pm.MediaId == mediaId).ExecuteDeleteAsync(cancellationToken);
        deleted += await db.ProjectVideos.Where(pm => pm.MediaId == mediaId).ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }

    public Task<ImageComposition?> GetImageCompositionAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.ImageCompositions.FirstOrDefaultAsync(composition => composition.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await db.Projects.AddAsync(project, cancellationToken);

    public async Task AddImageCompositionAsync(ImageComposition composition, CancellationToken cancellationToken = default) =>
        await db.ImageCompositions.AddAsync(composition, cancellationToken);

    public async Task AddImageCompositionRevisionAsync(ImageCompositionRevision revision, CancellationToken cancellationToken = default) =>
        await db.ImageCompositionRevisions.AddAsync(revision, cancellationToken);

    public async Task AddProjectUserAsync(ProjectUser projectUser, CancellationToken cancellationToken = default) =>
        await db.ProjectUsers.AddAsync(projectUser, cancellationToken);

    public void RemoveProjectUser(ProjectUser projectUser) => db.ProjectUsers.Remove(projectUser);

    public void Remove(Project project) => db.Projects.Remove(project);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

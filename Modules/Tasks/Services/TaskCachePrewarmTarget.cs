using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Repositories;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Tasks.Services;

internal sealed class TaskCachePrewarmTarget(
    ITaskRepository tasks,
    BusinessCache cache,
    CacheGenerationManager generations,
    CacheKeyFactory keys,
    IOptions<CachingOptions> options) : ICachePrewarmTarget
{
    private readonly TaskCacheOptions _feature = options.Value.Tasks;

    public CachePrewarmKind Kind => CachePrewarmKind.TaskReferences;

    public async Task<bool> PrewarmAsync(
        CachePrewarmRequest request,
        CancellationToken cancellationToken)
    {
        var current = await generations.GetAsync(
            "task-references", $"studio-{request.StudioId:N}", cancellationToken);
        if (!string.Equals(current, request.Generation, StringComparison.Ordinal))
        {
            return true;
        }

        var labels = (await tasks.ListLabelsAsync(request.StudioId, cancellationToken))
            .Select(item => new TaskLabelDto(
                item.Id, item.StudioId, item.Name, item.Color, item.CreatedAt, item.UpdatedAt))
            .ToList();
        var milestones = (await tasks.ListMilestonesAsync(request.StudioId, cancellationToken))
            .Select(item => new TaskMilestoneDto(
                item.Id,
                item.StudioId,
                item.Title,
                item.Description,
                item.DueDate,
                item.Status,
                item.CreatedAt,
                item.UpdatedAt))
            .ToList();

        current = await generations.GetAsync(
            "task-references", $"studio-{request.StudioId:N}", cancellationToken);
        if (!string.Equals(current, request.Generation, StringComparison.Ordinal))
        {
            return true;
        }

        var labelsWritten = await cache.TryWriteAsync(
            "tasks",
            _feature,
            BusinessCacheKey.Create(
                keys, "task-labels-v2", "studio", request.StudioId, "gen", request.Generation),
            TimeSpan.FromSeconds(_feature.ReferencesTtlSeconds),
            (IReadOnlyList<TaskLabelDto>)labels,
            cancellationToken);
        var milestonesWritten = await cache.TryWriteAsync(
            "tasks",
            _feature,
            BusinessCacheKey.Create(
                keys, "task-milestones-v2", "studio", request.StudioId, "gen", request.Generation),
            TimeSpan.FromSeconds(_feature.ReferencesTtlSeconds),
            (IReadOnlyList<TaskMilestoneDto>)milestones,
            cancellationToken);
        return labelsWritten && milestonesWritten;
    }
}

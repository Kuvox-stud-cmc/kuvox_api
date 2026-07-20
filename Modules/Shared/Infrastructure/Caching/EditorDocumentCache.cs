using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public sealed class EditorDocumentCache(
    BusinessCache cache,
    CacheKeyFactory keys,
    IOptions<CachingOptions> options)
{
    private readonly CacheFeatureOptions _documents = options.Value.EditorDocuments;
    private readonly CacheFeatureOptions _renderJobs = options.Value.RenderJobs;

    public bool DocumentsEnabled => cache.IsEnabled(_documents);
    public bool RenderJobsEnabled => cache.IsEnabled(_renderJobs);

    public string TimelineDocumentKey(Guid projectId, int revisionNumber) =>
        BusinessCacheKey.Create(keys, "timeline-document", "project", projectId, "revision", revisionNumber);

    public string TimelineListKey(Guid projectId, int revisionNumber) =>
        BusinessCacheKey.Create(keys, "timeline-list", "project", projectId, "revision", revisionNumber);

    public string ImageCompositionKey(Guid projectId, int revisionNumber) =>
        BusinessCacheKey.Create(keys, "image-composition", "project", projectId, "revision", revisionNumber);

    public string RenderJobKey(Guid jobId, string status, DateTimeOffset updatedAt) =>
        BusinessCacheKey.Create(keys, "render-job", "job", jobId, "status", status, "updated", updatedAt.UtcTicks);

    public Task<T> GetTimelineDocumentAsync<T>(Guid projectId, int revisionNumber, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("timeline-document", "read", _documents, TimelineDocumentKey(projectId, revisionNumber), DocumentTtl, factory, cancellationToken);

    public Task<T> GetTimelineListAsync<T>(Guid projectId, int revisionNumber, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("timeline-list", "read", _documents, TimelineListKey(projectId, revisionNumber), DocumentTtl, factory, cancellationToken);

    public Task<T> GetImageCompositionAsync<T>(Guid projectId, int revisionNumber, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("image-composition", "read", _documents, ImageCompositionKey(projectId, revisionNumber), DocumentTtl, factory, cancellationToken);

    public Task<T> GetRenderJobAsync<T>(Guid jobId, string status, DateTimeOffset updatedAt, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("render-job", "read", _renderJobs, RenderJobKey(jobId, status, updatedAt), RenderJobTtl, factory, cancellationToken);

    public Task WriteTimelineDocumentAsync<T>(Guid projectId, int revisionNumber, T value) =>
        cache.WriteAsync("timeline-document", _documents, TimelineDocumentKey(projectId, revisionNumber), DocumentTtl, value, CancellationToken.None);

    public Task WriteTimelineListAsync<T>(Guid projectId, int revisionNumber, T value) =>
        cache.WriteAsync("timeline-list", _documents, TimelineListKey(projectId, revisionNumber), DocumentTtl, value, CancellationToken.None);

    public Task WriteImageCompositionAsync<T>(Guid projectId, int revisionNumber, T value) =>
        cache.WriteAsync("image-composition", _documents, ImageCompositionKey(projectId, revisionNumber), DocumentTtl, value, CancellationToken.None);

    public Task WriteRenderJobAsync<T>(Guid jobId, string status, DateTimeOffset updatedAt, T value) =>
        cache.WriteAsync("render-job", _renderJobs, RenderJobKey(jobId, status, updatedAt), RenderJobTtl, value, CancellationToken.None);

    public Task DeleteRenderJobAsync(Guid jobId, string status, DateTimeOffset updatedAt) =>
        cache.InvalidateExactAsync("render-job", RenderJobKey(jobId, status, updatedAt), _renderJobs, CancellationToken.None);

    private TimeSpan DocumentTtl => TimeSpan.FromSeconds(_documents.TtlSeconds);
    private TimeSpan RenderJobTtl => TimeSpan.FromSeconds(_renderJobs.TtlSeconds);
}

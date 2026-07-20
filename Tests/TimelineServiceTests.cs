using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Enums;
using Kuvox.Api.Modules.Timelines.Models;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Timelines.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class TimelineServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly CallerContext Caller = new(UserId, []);

    [Fact]
    public async Task ListByProjectAsync_returns_empty_for_authorized_project_without_timeline()
    {
        var result = await CreateService(new FakeTimelineRepository())
            .ListByProjectAsync(ProjectId, Caller);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByProjectAsync_returns_authorized_timeline_summary()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);

        var result = await CreateService(repository).ListByProjectAsync(ProjectId, Caller);

        var item = Assert.Single(result);
        Assert.Equal(timeline.Id, item.Id);
        Assert.Equal(ProjectId, item.ProjectId);
    }

    [Fact]
    public async Task Warm_document_reads_reauthorize_and_only_repeat_the_revision_projection()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 1);
        var cache = CreateEnabledDocumentCache();
        var service = CreateService(repository, documentCache: cache);

        await service.GetCurrentDocumentAsync(ProjectId, Caller);
        await service.GetCurrentDocumentAsync(ProjectId, Caller);

        Assert.Equal(2, repository.RevisionIdentityReads);
        Assert.Equal(1, repository.FullRevisionReads);

        var revoked = CreateService(
            repository,
            new FakeProjectsApi { ReadException = DomainException.Forbidden("revoked") },
            documentCache: cache);
        await Assert.ThrowsAsync<DomainException>(() => revoked.GetCurrentDocumentAsync(ProjectId, Caller));
        Assert.Equal(1, repository.FullRevisionReads);
    }

    [Fact]
    public async Task Rendering_transition_deletes_only_the_exact_prior_status_key_after_commit()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var revision = repository.AddRevision(timeline.Id, 1);
        var priorUpdatedAt = new DateTimeOffset(2026, 7, 19, 1, 0, 0, TimeSpan.Zero);
        var job = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = UserId,
            Status = RenderStatus.Queued,
            UpdatedAt = priorUpdatedAt,
        };
        repository.RenderJobs.Add(job);
        var (cache, store) = CreateEnabledRenderCache();
        await cache.WriteRenderJobAsync(job.Id, "queued", priorUpdatedAt, new { status = "queued" });
        var priorKey = cache.RenderJobKey(job.Id, "queued", priorUpdatedAt);
        Assert.True(store.Contains(priorKey));

        await RenderingResultConsumer.ApplyStartedAsync(
            repository,
            new FakeRenderRealtimeNotifier(repository),
            cache,
            new RenderingStartedEvent(
                Guid.CreateVersion7(),
                "rendering.started",
                priorUpdatedAt.AddSeconds(1),
                Guid.CreateVersion7(),
                job.Id,
                priorUpdatedAt.AddSeconds(1)),
            CancellationToken.None);

        Assert.False(store.Contains(priorKey));
        Assert.Equal(RenderStatus.Rendering, job.Status);
    }

    [Fact]
    public async Task GetCurrentDocumentAsync_returns_latest_video_document()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var latest = repository.AddRevision(timeline.Id, 2);
        var service = CreateService(repository);

        var result = await service.GetCurrentDocumentAsync(ProjectId, Caller);

        Assert.Equal(ProjectId, result.ProjectId);
        Assert.Equal(timeline.Id, result.TimelineId);
        Assert.Equal(latest.Id, result.RevisionId);
        Assert.Equal(2, result.RevisionNumber);
        Assert.Equal(ProjectId.ToString(), result.DocumentJson.GetProperty("projectId").GetString());
    }

    [Fact]
    public async Task SaveCurrentDocumentAsync_creates_revision_when_base_revision_matches()
    {
        var repository = new FakeTimelineRepository();
        var service = CreateService(repository);

        var result = await service.SaveCurrentDocumentAsync(
            ProjectId,
            Caller,
            SaveRequest(ProjectId, baseRevisionNumber: 0, operationsJson: """[{"id":"op-1","type":"moveItem"}]"""));

        Assert.Equal(1, result.RevisionNumber);
        Assert.Single(repository.Timelines);
        Assert.Single(repository.Revisions);
        Assert.True(repository.SaveChangesCalled);
        Assert.Contains("op-1", repository.Revisions[0].OperationsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveCurrentDocumentAsync_rejects_stale_revision()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 2);
        var service = CreateService(repository);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.SaveCurrentDocumentAsync(ProjectId, Caller, SaveRequest(ProjectId, baseRevisionNumber: 1)));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Empty(repository.RenderJobs);
    }

    [Fact]
    public async Task SaveCurrentDocumentAsync_rejects_new_inaccessible_media()
    {
        var mediaId = Guid.NewGuid();
        var repository = new FakeTimelineRepository();
        var media = new FakeMediaApi
        {
            Resolutions =
            [
                new MediaResolution(mediaId, MediaKind.Video, MediaResolutionAvailability.Inaccessible, null),
            ],
        };
        var service = CreateService(repository, media: media);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.SaveCurrentDocumentAsync(
            ProjectId,
            Caller,
            SaveRequest(ProjectId, 0, documentJson: DocumentWithMedia(ProjectId, mediaId))));

        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Empty(repository.Revisions);
    }

    [Fact]
    public async Task SaveCurrentDocumentAsync_allows_existing_inaccessible_media_to_remain()
    {
        var mediaId = Guid.NewGuid();
        var document = DocumentWithMedia(ProjectId, mediaId);
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 1, document);
        var media = new FakeMediaApi
        {
            Resolutions =
            [
                new MediaResolution(mediaId, MediaKind.Video, MediaResolutionAvailability.Inaccessible, null),
            ],
        };
        var service = CreateService(repository, media: media);

        var result = await service.SaveCurrentDocumentAsync(
            ProjectId,
            Caller,
            SaveRequest(ProjectId, 1, documentJson: document));

        Assert.Equal(2, result.RevisionNumber);
    }

    [Fact]
    public async Task RequestRenderAsync_queues_job_for_latest_revision()
    {
        var mediaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var revision = repository.AddRevision(timeline.Id, 3, DocumentWithMedia(ProjectId, mediaId));
        var realtime = new FakeRenderRealtimeNotifier(repository);
        var service = CreateService(repository, realtime: realtime);

        var result = await service.RequestRenderAsync(
            timeline.Id,
            Caller,
            new RenderTimelineRequest(timeline.Id, 3, Json("""{"format":"mp4","width":1920,"height":1080,"frameRate":30,"quality":"standard"}""")));

        Assert.Equal("queued", result.Status);
        Assert.Equal(revision.Id, result.RevisionId);
        Assert.Equal(3, result.RevisionNumber);
        Assert.Single(repository.RenderJobs);
        Assert.Equal(RenderStatus.Queued, repository.RenderJobs[0].Status);
        Assert.Single(repository.OutboxMessages);
        Assert.Equal("kuvox.rendering", repository.OutboxMessages[0].RoutingKey);
        Assert.Equal("rendering.requested", repository.OutboxMessages[0].EventType);
        Assert.Equal("kuvox-renders", repository.RenderJobs[0].OutputBucketName);
        using var requested = JsonDocument.Parse(repository.OutboxMessages[0].PayloadJson);
        Assert.Equal("kuvox-renders", requested.RootElement.GetProperty("outputBucketName").GetString());
        var mediaSources = requested.RootElement.GetProperty("mediaSources");
        Assert.NotEmpty(mediaSources.EnumerateArray());
        Assert.Equal("kuvox-canonical", mediaSources[0].GetProperty("bucketName").GetString());
        Assert.Equal("media/33333333-3333-3333-3333-333333333333/canonical.mp4", mediaSources[0].GetProperty("objectKey").GetString());
        Assert.Single(realtime.Updates);
        Assert.Equal("queued", realtime.Updates[0].Status);
        Assert.Equal([1], realtime.SaveCountsAtPublish);
    }

    [Fact]
    public async Task RequestRenderAsync_rejects_unready_media()
    {
        var mediaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 1, DocumentWithMedia(ProjectId, mediaId));
        var media = new FakeMediaApi
        {
            Resolutions =
            [
                new MediaResolution(mediaId, MediaKind.Video, MediaResolutionAvailability.Processing, null),
            ],
        };
        var service = CreateService(repository, media: media);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.RequestRenderAsync(
                timeline.Id,
                Caller,
                new RenderTimelineRequest(timeline.Id, 1, Json("""{"format":"mp4","width":1920,"height":1080,"frameRate":30,"quality":"standard"}"""))));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Empty(repository.RenderJobs);
        Assert.Empty(repository.OutboxMessages);
    }

    [Fact]
    public async Task Rendering_result_events_update_job_idempotently()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var revision = repository.AddRevision(timeline.Id, 2);
        var job = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = UserId,
            Status = RenderStatus.Queued,
        };
        repository.RenderJobs.Add(job);
        var realtime = new FakeRenderRealtimeNotifier(repository);
        var sourceEventId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await RenderingResultConsumer.ApplyStartedAsync(
            repository,
            realtime,
            new RenderingStartedEvent(
                Guid.CreateVersion7(),
                "rendering.started",
                DateTimeOffset.UtcNow,
                sourceEventId,
                job.Id,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(RenderStatus.Rendering, job.Status);
        Assert.NotNull(job.StartedAt);

        await RenderingResultConsumer.ApplyStartedAsync(
            repository,
            realtime,
            new RenderingStartedEvent(
                Guid.CreateVersion7(),
                "rendering.started",
                DateTimeOffset.UtcNow,
                sourceEventId,
                job.Id,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await RenderingResultConsumer.ApplyCompletedAsync(
            repository,
            realtime,
            new RenderingCompletedEvent(
                Guid.CreateVersion7(),
                "rendering.completed",
                DateTimeOffset.UtcNow,
                sourceEventId,
                job.Id,
                "kuvox-renders",
                "renders/job.mp4",
                "video/mp4",
                456,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(RenderStatus.Completed, job.Status);
        Assert.Equal("renders/job.mp4", job.OutputStorageKey);
        Assert.Equal(456, job.OutputSizeBytes);

        await RenderingResultConsumer.ApplyFailedAsync(
            repository,
            realtime,
            new RenderingFailedEvent(
                Guid.CreateVersion7(),
                "rendering.failed",
                DateTimeOffset.UtcNow,
                sourceEventId,
                job.Id,
                "LateFailure",
                "late failure",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(RenderStatus.Completed, job.Status);
        Assert.Null(job.ErrorCode);

        var failedJob = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = UserId,
            Status = RenderStatus.Queued,
        };
        repository.RenderJobs.Add(failedJob);
        var failure = new RenderingFailedEvent(
            Guid.CreateVersion7(),
            "rendering.failed",
            DateTimeOffset.UtcNow,
            sourceEventId,
            failedJob.Id,
            "EncoderFailed",
            "Encoder failed.",
            DateTimeOffset.UtcNow);
        await RenderingResultConsumer.ApplyFailedAsync(repository, realtime, failure, CancellationToken.None);
        await RenderingResultConsumer.ApplyFailedAsync(repository, realtime, failure, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, failedJob.Status);
        Assert.Equal(3, realtime.Updates.Count);
        Assert.Equal(["rendering", "completed", "failed"], realtime.Updates.Select(update => update.Status));
        Assert.Equal([1, 2, 3], realtime.SaveCountsAtPublish);
    }

    [Fact]
    public async Task GetRenderJobOutputAsync_streams_only_completed_outputs()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var revision = repository.AddRevision(timeline.Id, 2);
        var storage = new FakeFileStorageService();
        var queued = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = UserId,
            Status = RenderStatus.Queued,
            OutputBucketName = "kuvox-renders",
            OutputStorageKey = "renders/queued.mp4",
            OutputContentType = "video/mp4",
        };
        var completed = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = UserId,
            Status = RenderStatus.Completed,
            OutputBucketName = "kuvox-renders",
            OutputStorageKey = "renders/completed.mp4",
            OutputContentType = "video/mp4",
        };
        repository.RenderJobs.AddRange([queued, completed]);
        var service = CreateService(repository, storage: storage);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.GetRenderJobOutputAsync(queued.Id, Caller));
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);

        var output = await service.GetRenderJobOutputAsync(completed.Id, Caller);

        Assert.Equal("video/mp4", output.ContentType);
        Assert.Equal("kuvox-render.mp4", output.FileName);
        Assert.Equal(("kuvox-renders", "renders/completed.mp4"), storage.LastDownload);
    }

    [Fact]
    public async Task RenderRealtimeNotifier_targets_requester_without_storage_metadata()
    {
        var clients = new RecordingHubClients();
        var notifier = new RenderRealtimeNotifier(
            new FakeHubContext(clients),
            NullLogger<RenderRealtimeNotifier>.Instance);
        var job = new RenderJob
        {
            TimelineId = Guid.NewGuid(),
            RequestedByUserId = UserId,
            Status = RenderStatus.Completed,
            OutputBucketName = "private-bucket",
            OutputStorageKey = "private/render.mp4",
            OutputContentType = "video/mp4",
            OutputSizeBytes = 456,
        };

        await notifier.RenderJobUpdatedAsync(job);

        Assert.Equal($"user:{UserId}", clients.LastGroup);
        Assert.Equal("renderJobUpdated", clients.Proxy.Method);
        var payload = Assert.IsType<RenderRealtimeUpdate>(Assert.Single(clients.Proxy.Arguments));
        Assert.True(payload.OutputAvailable);
        var json = JsonSerializer.Serialize(payload);
        Assert.DoesNotContain("bucket", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestRenderAsync_queues_an_older_existing_revision_with_its_exact_snapshot()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        const string olderDocument = """{"projectId":"11111111-1111-1111-1111-111111111111","settings":{"width":1280},"media":{},"tracks":[],"marker":"older"}""";
        var older = repository.AddRevision(timeline.Id, 3, olderDocument);
        repository.AddRevision(timeline.Id, 4, """{"projectId":"11111111-1111-1111-1111-111111111111","settings":{"width":1920},"media":{},"tracks":[],"marker":"newer"}""");
        var service = CreateService(repository);

        var result = await service.RequestRenderAsync(
            timeline.Id,
            Caller,
            new RenderTimelineRequest(timeline.Id, 3, Json("""{"format":"mp4","width":1920,"height":1080,"frameRate":30,"quality":"standard"}""")));

        Assert.Equal(older.Id, result.RevisionId);
        Assert.Equal(3, result.RevisionNumber);
        using var requested = JsonDocument.Parse(Assert.Single(repository.OutboxMessages).PayloadJson);
        Assert.Equal(3, requested.RootElement.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("older", requested.RootElement.GetProperty("documentJson").GetProperty("marker").GetString());
    }

    [Fact]
    public async Task RecordPerformanceAsync_validates_metric_names_and_ranges()
    {
        var service = CreateService(new FakeTimelineRepository());

        await service.RecordPerformanceAsync(
            ProjectId,
            Caller,
            new RecordVideoEditorPerformanceRequest([
                new VideoEditorPerformanceMetricDto("timeline-drag-latency", 12.4, DateTimeOffset.UtcNow, 3, 5, 5, 20),
            ]));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.RecordPerformanceAsync(
                ProjectId,
                Caller,
                new RecordVideoEditorPerformanceRequest([
                    new VideoEditorPerformanceMetricDto("unknown", 1, DateTimeOffset.UtcNow, null, null, null, null),
                ])));
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Write_operations_require_write_access_but_read_load_allows_viewers()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 1);
        var projects = new FakeProjectsApi { WriteException = DomainException.Forbidden("viewer") };
        var service = CreateService(repository, projects);

        var current = await service.GetCurrentDocumentAsync(ProjectId, Caller);
        Assert.Equal(1, current.RevisionNumber);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.SaveCurrentDocumentAsync(ProjectId, Caller, SaveRequest(ProjectId, baseRevisionNumber: 1)));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Video_document_endpoints_reject_image_projects()
    {
        var repository = new FakeTimelineRepository();
        var projects = new FakeProjectsApi
        {
            ReadAccess = new ProjectDocumentAccess(ProjectId, ProjectContentKind.Image, "Image Project", DateTimeOffset.UtcNow),
        };
        var service = CreateService(repository, projects);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.GetCurrentDocumentAsync(ProjectId, Caller));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    private static TimelineService CreateService(
        FakeTimelineRepository repository,
        FakeProjectsApi? projects = null,
        FakeMediaApi? media = null,
        FakeFileStorageService? storage = null,
        FakeRenderRealtimeNotifier? realtime = null,
        EditorDocumentCache? documentCache = null) =>
        new(
            repository,
            projects ?? new FakeProjectsApi(),
            media ?? new FakeMediaApi(),
            storage ?? new FakeFileStorageService(),
            realtime ?? new FakeRenderRealtimeNotifier(repository),
            documentCache ?? CreateDocumentCache(),
            Options.Create(new RabbitMqOptions { ExchangeName = "kuvox.events" }),
            Options.Create(new StorageOptions { RawBucketName = "kuvox-renders" }),
            NullLogger<TimelineService>.Instance);

    private static EditorDocumentCache CreateDocumentCache()
    {
        var options = new CachingOptions();
        var store = new DisabledCacheStore();
        var codec = new JsonCacheCodec(new SystemCacheClock());
        var business = new BusinessCache(store, codec, Options.Create(options), NullLogger<BusinessCache>.Instance);
        return new EditorDocumentCache(business, new CacheKeyFactory(options), Options.Create(options));
    }

    private static EditorDocumentCache CreateEnabledDocumentCache()
    {
        var options = new CachingOptions
        {
            Enabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            EditorDocuments = new CacheFeatureOptions { Enabled = true, TtlSeconds = 15 },
        };
        var business = new BusinessCache(
            new TestCacheStore(),
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance);
        return new EditorDocumentCache(business, new CacheKeyFactory(options), Options.Create(options));
    }

    private static (EditorDocumentCache Cache, TestCacheStore Store) CreateEnabledRenderCache()
    {
        var options = new CachingOptions
        {
            Enabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            RenderJobs = new CacheFeatureOptions { Enabled = true, TtlSeconds = 3 },
        };
        var store = new TestCacheStore();
        var business = new BusinessCache(
            store,
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance);
        return (new EditorDocumentCache(business, new CacheKeyFactory(options), Options.Create(options)), store);
    }

    private static SaveTimelineDocumentRequest SaveRequest(
        Guid projectId,
        int baseRevisionNumber,
        string operationsJson = "[]",
        string? documentJson = null) =>
        new(
            Json(documentJson ?? DocumentJson(projectId)),
            Json(operationsJson),
            baseRevisionNumber,
            1,
            "manual",
            "Save");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string DocumentJson(Guid projectId, int revision = 0) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            projectId = projectId.ToString(),
            name = "Test",
            createdAt = "2026-01-01T00:00:00.000Z",
            updatedAt = "2026-01-01T00:00:00.000Z",
            settings = new
            {
                width = 1920,
                height = 1080,
                aspectRatio = "16:9",
                frameRate = 30,
                previewQuality = "balanced",
                defaultTransitionDuration = 0.4,
                exportPreset = "h264-1080p",
            },
            media = new { },
            tracks = Array.Empty<object>(),
            transitions = Array.Empty<object>(),
            effects = Array.Empty<object>(),
            history = new
            {
                revision,
                canUndo = false,
                canRedo = false,
            },
        });

    private static string DocumentWithMedia(Guid projectId, Guid mediaId) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            projectId = projectId.ToString(),
            name = "Test",
            createdAt = "2026-01-01T00:00:00.000Z",
            updatedAt = "2026-01-01T00:00:00.000Z",
            settings = new
            {
                width = 1920,
                height = 1080,
                aspectRatio = "16:9",
                frameRate = 30,
                previewQuality = "balanced",
                defaultTransitionDuration = 0.4,
                exportPreset = "h264-1080p",
            },
            media = new Dictionary<string, object>
            {
                [mediaId.ToString()] = new
                {
                    id = mediaId.ToString(),
                    kind = "video",
                    name = "clip.mp4",
                    duration = 10,
                },
            },
            tracks = new[]
            {
                new
                {
                    id = "v1",
                    kind = "video",
                    items = new[]
                    {
                        new
                        {
                            id = "item-1",
                            type = "video",
                            mediaId = mediaId.ToString(),
                            timelineStart = 0,
                            duration = 5,
                            sourceIn = 0,
                            sourceOut = 5,
                        },
                    },
                },
            },
            transitions = Array.Empty<object>(),
            effects = Array.Empty<object>(),
            history = new
            {
                revision = 1,
                canUndo = false,
                canRedo = false,
            },
        });

    private sealed class FakeProjectsApi : IProjectsApi
    {
        public ProjectDocumentAccess ReadAccess { get; init; } = new(ProjectId, ProjectContentKind.Video, "Video Project", DateTimeOffset.UtcNow);
        public ProjectDocumentAccess WriteAccess { get; init; } = new(ProjectId, ProjectContentKind.Video, "Video Project", DateTimeOffset.UtcNow);
        public DomainException? WriteException { get; init; }
        public DomainException? ReadException { get; init; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == ProjectId);
        public Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<ProjectSummary?>(null);
        public Task<int> CountByWorkspaceAsync(Guid ownerId, ProjectOwnerKind ownerKind, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<ProjectDocumentAccess> RequireReadAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default) =>
            ReadException is not null ? Task.FromException<ProjectDocumentAccess>(ReadException) : Task.FromResult(ReadAccess);

        public Task<ProjectDocumentAccess> RequireWriteAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default) =>
            WriteException is not null ? Task.FromException<ProjectDocumentAccess>(WriteException) : Task.FromResult(WriteAccess);
    }

    private sealed class FakeMediaApi : IMediaApi
    {
        public IReadOnlyList<MediaResolution> Resolutions { get; init; } = [];

        public Task<MediaSummary?> GetSummaryAsync(Guid mediaId, CancellationToken cancellationToken = default) => Task.FromResult<MediaSummary?>(null);

        public Task<IReadOnlyList<MediaResolution>> ResolveAsync(
            IReadOnlyCollection<Guid> mediaIds,
            CallerContext caller,
            CancellationToken cancellationToken = default)
        {
            if (Resolutions.Count > 0)
            {
                return Task.FromResult(Resolutions);
            }

            return Task.FromResult<IReadOnlyList<MediaResolution>>(mediaIds
                .Select(mediaId => new MediaResolution(
                    mediaId,
                    MediaKind.Video,
                    MediaResolutionAvailability.Available,
                    new MediaSummary(
                        mediaId,
                        ProjectId,
                        OwnerKind.User,
                        MediaKind.Video,
                        "clip.mp4",
                        "Ready",
                        CanonicalBucketName: "kuvox-canonical",
                        CanonicalStorageKey: $"media/{mediaId}/canonical.mp4",
                        SizeBytes: 123,
                        DurationSeconds: 10)))
                .ToList());
        }

        public Task<MediaWorkspaceUsageSummary> GetWorkspaceUsageAsync(Guid ownerId, OwnerKind ownerKind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaWorkspaceUsageSummary(0, 0));
    }

    private sealed class FakeTimelineRepository : ITimelineRepository
    {
        public List<Timeline> Timelines { get; } = [];
        public List<TimelineRevision> Revisions { get; } = [];
        public List<RenderJob> RenderJobs { get; } = [];
        public List<OutboxMessage> OutboxMessages { get; } = [];
        public bool SaveChangesCalled { get; private set; }
        public int SaveChangesCount { get; private set; }
        public int RevisionIdentityReads { get; private set; }
        public int FullRevisionReads { get; private set; }

        public Timeline AddTimeline(Guid projectId)
        {
            var timeline = new Timeline { ProjectId = projectId, Name = "Current" };
            Timelines.Add(timeline);
            return timeline;
        }

        public TimelineRevision AddRevision(Guid timelineId, int revisionNumber, string? documentJson = null)
        {
            var revision = new TimelineRevision
            {
                TimelineId = timelineId,
                RevisionNumber = revisionNumber,
                DocumentJson = documentJson ?? DocumentJson(ProjectId, revisionNumber),
                DocumentSchemaVersion = 1,
                OperationsJson = "[]",
                CreatedByUserId = UserId,
            };
            Revisions.Add(revision);
            return revision;
        }

        public Task<Timeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Timelines.FirstOrDefault(t => t.Id == id));
        public Task<Timeline?> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(Timelines.FirstOrDefault(t => t.ProjectId == projectId));
        public Task<IReadOnlyList<Timeline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Timeline>>(Timelines.Where(t => t.ProjectId == projectId).ToList());
        public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(Timelines.Count(t => t.ProjectId == projectId));
        public Task<TimelineRevision?> GetLatestRevisionAsync(Guid timelineId, CancellationToken cancellationToken = default) => Task.FromResult(Revisions.Where(r => r.TimelineId == timelineId).OrderByDescending(r => r.RevisionNumber).FirstOrDefault());
        public Task<TimelineRevisionIdentity?> GetCurrentRevisionIdentityAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            RevisionIdentityReads++;
            var timeline = Timelines.FirstOrDefault(t => t.ProjectId == projectId);
            var revision = timeline is null ? null : Revisions.Where(r => r.TimelineId == timeline.Id).OrderByDescending(r => r.RevisionNumber).FirstOrDefault();
            return Task.FromResult(timeline is null || revision is null
                ? null
                : new TimelineRevisionIdentity(projectId, timeline.Id, timeline.Name, timeline.CreatedAt, timeline.UpdatedAt, revision.Id, revision.RevisionNumber));
        }
        public Task<TimelineRevision?> GetRevisionByNumberAsync(Guid timelineId, int revisionNumber, CancellationToken cancellationToken = default) => Task.FromResult(Revisions.FirstOrDefault(r => r.TimelineId == timelineId && r.RevisionNumber == revisionNumber));
        public Task<TimelineRevision?> GetRevisionByIdAsync(Guid revisionId, CancellationToken cancellationToken = default)
        {
            FullRevisionReads++;
            return Task.FromResult(Revisions.FirstOrDefault(r => r.Id == revisionId));
        }
        public Task<RenderJob?> GetRenderJobByIdAsync(Guid renderJobId, CancellationToken cancellationToken = default) => Task.FromResult(RenderJobs.FirstOrDefault(j => j.Id == renderJobId));
        public Task<RenderJobAccessState?> GetRenderJobAccessStateAsync(Guid renderJobId, CancellationToken cancellationToken = default)
        {
            var job = RenderJobs.FirstOrDefault(j => j.Id == renderJobId);
            var timeline = job is null ? null : Timelines.FirstOrDefault(t => t.Id == job.TimelineId);
            var revision = job?.RevisionId is { } revisionId ? Revisions.FirstOrDefault(r => r.Id == revisionId) : null;
            return Task.FromResult(job is null || timeline is null
                ? null
                : new RenderJobAccessState(timeline.ProjectId, timeline.Id, job.RevisionId, revision?.RevisionNumber, job.Status, job.UpdatedAt));
        }
        public Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default) { Timelines.Add(timeline); return Task.CompletedTask; }
        public Task AddRevisionAsync(TimelineRevision revision, CancellationToken cancellationToken = default) { Revisions.Add(revision); return Task.CompletedTask; }
        public Task AddRenderJobAsync(RenderJob renderJob, CancellationToken cancellationToken = default) { RenderJobs.Add(renderJob); return Task.CompletedTask; }
        public Task EnqueueOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default) { OutboxMessages.Add(message); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { SaveChangesCalled = true; SaveChangesCount++; return Task.CompletedTask; }
    }

    private sealed class TestCacheStore : ICacheStore
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public bool Contains(string key) => _values.ContainsKey(key);

        public Task<CacheReadResult> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value)
                ? new CacheReadResult(CacheReadOutcome.Hit, value)
                : new CacheReadResult(CacheReadOutcome.Miss));

        public Task<CacheWriteOutcome> SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _values[key] = value.ToArray();
            return Task.FromResult(CacheWriteOutcome.Success);
        }

        public Task<CacheWriteOutcome> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.FromResult(CacheWriteOutcome.Success);
        }
    }

    private sealed class FakeRenderRealtimeNotifier(FakeTimelineRepository repository) : IRenderRealtimeNotifier
    {
        public List<RenderRealtimeUpdate> Updates { get; } = [];
        public List<int> SaveCountsAtPublish { get; } = [];

        public Task RenderJobUpdatedAsync(RenderJob job, CancellationToken cancellationToken = default)
        {
            Assert.True(repository.SaveChangesCalled);
            Updates.Add(RenderRealtimeUpdate.FromJob(job));
            SaveCountsAtPublish.Add(repository.SaveChangesCount);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public (string BucketName, string ObjectKey)? LastDownload { get; private set; }

        public Task<StoredMediaObject> UploadRawAsync(IFormFile file, Guid mediaId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<DownloadedMediaObject> DownloadAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            LastDownload = (bucketName, objectKey);
            return Task.FromResult(new DownloadedMediaObject(
                new MemoryStream([1, 2, 3]),
                "video/mp4",
                3,
                "\"etag\""));
        }

        public Task<bool> ExistsAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeHubContext(RecordingHubClients clients) : IHubContext<MediaHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups { get; } = new NoopGroupManager();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public RecordingClientProxy Proxy { get; } = new();
        public string? LastGroup { get; private set; }
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) { LastGroup = groupName; return Proxy; }
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[] Arguments { get; private set; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Arguments = args;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

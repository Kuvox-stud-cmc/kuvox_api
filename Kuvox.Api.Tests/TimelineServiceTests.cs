using System.Text.Json;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Enums;
using Kuvox.Api.Modules.Timelines.Models;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Timelines.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kuvox.Api.Tests;

public sealed class TimelineServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly CallerContext Caller = new(UserId, []);

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
    public async Task RequestRenderAsync_queues_job_for_latest_revision()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        var revision = repository.AddRevision(timeline.Id, 3);
        var service = CreateService(repository);

        var result = await service.RequestRenderAsync(
            timeline.Id,
            Caller,
            new RenderTimelineRequest(timeline.Id, 3, Json("""{"format":"mp4","width":1920,"height":1080,"frameRate":30,"quality":"standard"}""")));

        Assert.Equal("queued", result.Status);
        Assert.Equal(revision.Id, result.RevisionId);
        Assert.Equal(3, result.RevisionNumber);
        Assert.Single(repository.RenderJobs);
        Assert.Equal(RenderStatus.Queued, repository.RenderJobs[0].Status);
    }

    [Fact]
    public async Task RequestRenderAsync_rejects_stale_revision()
    {
        var repository = new FakeTimelineRepository();
        var timeline = repository.AddTimeline(ProjectId);
        repository.AddRevision(timeline.Id, 4);
        var service = CreateService(repository);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.RequestRenderAsync(
                timeline.Id,
                Caller,
                new RenderTimelineRequest(timeline.Id, 3, Json("""{"format":"mp4","width":1920,"height":1080,"frameRate":30,"quality":"standard"}"""))));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
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

    private static TimelineService CreateService(FakeTimelineRepository repository, FakeProjectsApi? projects = null) =>
        new(repository, projects ?? new FakeProjectsApi(), NullLogger<TimelineService>.Instance);

    private static SaveTimelineDocumentRequest SaveRequest(Guid projectId, int baseRevisionNumber, string operationsJson = "[]") =>
        new(
            Json(DocumentJson(projectId)),
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

    private sealed class FakeProjectsApi : IProjectsApi
    {
        public ProjectDocumentAccess ReadAccess { get; init; } = new(ProjectId, ProjectContentKind.Video, "Video Project", DateTimeOffset.UtcNow);
        public ProjectDocumentAccess WriteAccess { get; init; } = new(ProjectId, ProjectContentKind.Video, "Video Project", DateTimeOffset.UtcNow);
        public DomainException? WriteException { get; init; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == ProjectId);
        public Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<ProjectSummary?>(null);
        public Task<int> CountByWorkspaceAsync(Guid ownerId, ProjectOwnerKind ownerKind, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<ProjectDocumentAccess> RequireReadAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default) => Task.FromResult(ReadAccess);

        public Task<ProjectDocumentAccess> RequireWriteAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default) =>
            WriteException is not null ? Task.FromException<ProjectDocumentAccess>(WriteException) : Task.FromResult(WriteAccess);
    }

    private sealed class FakeTimelineRepository : ITimelineRepository
    {
        public List<Timeline> Timelines { get; } = [];
        public List<TimelineRevision> Revisions { get; } = [];
        public List<RenderJob> RenderJobs { get; } = [];
        public bool SaveChangesCalled { get; private set; }

        public Timeline AddTimeline(Guid projectId)
        {
            var timeline = new Timeline { ProjectId = projectId, Name = "Current" };
            Timelines.Add(timeline);
            return timeline;
        }

        public TimelineRevision AddRevision(Guid timelineId, int revisionNumber)
        {
            var revision = new TimelineRevision
            {
                TimelineId = timelineId,
                RevisionNumber = revisionNumber,
                DocumentJson = DocumentJson(ProjectId, revisionNumber),
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
        public Task<TimelineRevision?> GetRevisionByNumberAsync(Guid timelineId, int revisionNumber, CancellationToken cancellationToken = default) => Task.FromResult(Revisions.FirstOrDefault(r => r.TimelineId == timelineId && r.RevisionNumber == revisionNumber));
        public Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default) { Timelines.Add(timeline); return Task.CompletedTask; }
        public Task AddRevisionAsync(TimelineRevision revision, CancellationToken cancellationToken = default) { Revisions.Add(revision); return Task.CompletedTask; }
        public Task AddRenderJobAsync(RenderJob renderJob, CancellationToken cancellationToken = default) { RenderJobs.Add(renderJob); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { SaveChangesCalled = true; return Task.CompletedTask; }
    }
}

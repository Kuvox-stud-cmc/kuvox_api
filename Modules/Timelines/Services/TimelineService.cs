using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Enums;
using Kuvox.Api.Modules.Timelines.Models;
using Kuvox.Api.Modules.Timelines.Repositories;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Timelines.Services;

internal sealed class TimelineService(
    ITimelineRepository timelines,
    IProjectsApi projects,
    IMediaApi media,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<TimelineService> logger) : ITimelineService
{
    private readonly ITimelineRepository _timelines = timelines;
    private readonly IProjectsApi _projects = projects;
    private readonly IMediaApi _media = media;
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private readonly StorageOptions _storageOptions = storageOptions.Value;
    private readonly ILogger<TimelineService> _logger = logger;

    private const string RenderingRequestedEventType = "rendering.requested";
    private const string RenderingRequestedRoutingKey = "kuvox.rendering";

    private static readonly HashSet<string> SupportedRenderFormats = new(StringComparer.Ordinal)
    {
        "mp4",
        "mov",
    };

    private static readonly HashSet<int> SupportedRenderFrameRates = [24, 25, 30, 60];

    private static readonly HashSet<string> SupportedRenderQualities = new(StringComparer.Ordinal)
    {
        "draft",
        "standard",
        "high",
    };

    private static readonly HashSet<string> SupportedPerformanceMetricNames = new(StringComparer.Ordinal)
    {
        "editor-open",
        "first-usable-editor",
        "timeline-drag-latency",
        "playback-seek-latency",
    };

    public async Task<TimelineDocumentDto> GetCurrentDocumentAsync(
        Guid projectId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.RequireReadAccessAsync(projectId, caller, cancellationToken);
        RequireVideoProject(project);

        var timeline = await _timelines.GetByProjectAsync(project.Id, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");
        var latestRevision = await _timelines.GetLatestRevisionAsync(timeline.Id, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");

        _logger.LogInformation(
            "VideoTimelineGet ProjectId={ProjectId} TimelineId={TimelineId} RevisionId={RevisionId} RevisionNumber={RevisionNumber}",
            project.Id,
            timeline.Id,
            latestRevision.Id,
            latestRevision.RevisionNumber);

        return ToDocumentDto(project.Id, timeline.Id, latestRevision);
    }

    public async Task<TimelineDocumentDto> SaveCurrentDocumentAsync(
        Guid projectId,
        CallerContext caller,
        SaveTimelineDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.RequireWriteAccessAsync(projectId, caller, cancellationToken);
        RequireVideoProject(project);
        ValidateSaveRequest(project.Id, request);

        var now = DateTimeOffset.UtcNow;
        var documentJson = request.DocumentJson.GetRawText();
        var operationsJson = request.OperationsJson.GetRawText();
        var operationIds = OperationIds(request.OperationsJson);
        var operationCount = OperationCount(request.OperationsJson);
        var timeline = await _timelines.GetByProjectAsync(project.Id, cancellationToken);
        var latestRevision = timeline is null
            ? null
            : await _timelines.GetLatestRevisionAsync(timeline.Id, cancellationToken);
        var latestRevisionNumber = latestRevision?.RevisionNumber ?? 0;
        if (request.BaseRevisionNumber != latestRevisionNumber)
        {
            _logger.LogWarning(
                "VideoTimelineSaveConflict ProjectId={ProjectId} TimelineId={TimelineId} BaseRevisionNumber={BaseRevisionNumber} ServerRevisionNumber={ServerRevisionNumber} OperationCount={OperationCount} OperationIds={OperationIds}",
                project.Id,
                timeline?.Id,
                request.BaseRevisionNumber,
                latestRevisionNumber,
                operationCount,
                operationIds);
            throw DomainException.Conflict("The video timeline changed on the server.");
        }

        if (timeline is null)
        {
            timeline = new Timeline
            {
                ProjectId = project.Id,
                Name = "Current video timeline",
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _timelines.AddAsync(timeline, cancellationToken);
        }

        var revision = new TimelineRevision
        {
            TimelineId = timeline.Id,
            RevisionNumber = latestRevisionNumber + 1,
            DocumentJson = documentJson,
            DocumentSchemaVersion = request.DocumentSchemaVersion,
            OperationsJson = operationsJson,
            Operations = operationsJson,
            Source = NormalizeOptionalText(request.Source, 64),
            Label = NormalizeOptionalText(request.Label, 200),
            CreatedByUserId = caller.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        timeline.UpdatedAt = now;
        await _timelines.AddRevisionAsync(revision, cancellationToken);
        await _timelines.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "VideoTimelineSaveSuccess ProjectId={ProjectId} TimelineId={TimelineId} RevisionId={RevisionId} RevisionNumber={RevisionNumber} OperationCount={OperationCount} OperationIds={OperationIds}",
            project.Id,
            timeline.Id,
            revision.Id,
            revision.RevisionNumber,
            operationCount,
            operationIds);

        return ToDocumentDto(project.Id, timeline.Id, revision);
    }

    public Task<IReadOnlyList<TimelineDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TimelineDto> CreateAsync(CreateTimelineRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TimelineRevisionDto> AddRevisionAsync(Guid timelineId, CreateRevisionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public async Task<RenderJobDto> RequestRenderAsync(
        Guid timelineId,
        CallerContext caller,
        RenderTimelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TimelineId != timelineId)
        {
            throw DomainException.BadRequest("Render request timelineId must match the route timeline id.");
        }

        ValidateRenderSettings(request.Settings);

        var timeline = await _timelines.GetByIdAsync(timelineId, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");
        var project = await _projects.RequireWriteAccessAsync(timeline.ProjectId, caller, cancellationToken);
        RequireVideoProject(project);

        var latestRevision = await _timelines.GetLatestRevisionAsync(timeline.Id, cancellationToken)
            ?? throw DomainException.NotFound("Timeline revision not found.");

        if (request.RevisionNumber < latestRevision.RevisionNumber)
        {
            _logger.LogWarning(
                "VideoTimelineRenderConflict ProjectId={ProjectId} TimelineId={TimelineId} RequestedRevisionNumber={RequestedRevisionNumber} ServerRevisionId={ServerRevisionId} ServerRevisionNumber={ServerRevisionNumber}",
                timeline.ProjectId,
                timeline.Id,
                request.RevisionNumber,
                latestRevision.Id,
                latestRevision.RevisionNumber);
            throw DomainException.Conflict("Render request revision is not the latest synced timeline revision.");
        }

        if (request.RevisionNumber > latestRevision.RevisionNumber)
        {
            throw DomainException.NotFound("Timeline revision not found.");
        }

        var revision = await _timelines.GetRevisionByNumberAsync(timeline.Id, request.RevisionNumber, cancellationToken)
            ?? throw DomainException.NotFound("Timeline revision not found.");

        var now = DateTimeOffset.UtcNow;
        var mediaSources = await ResolveReferencedMediaAsync(revision.DocumentJson, caller, cancellationToken);
        var format = request.Settings.GetProperty("format").GetString() ?? "mp4";
        var outputContentType = format == "mov"
            ? "video/quicktime"
            : "video/mp4";
        var outputBucketName = _storageOptions.RawBucketName;
        var renderJob = new RenderJob
        {
            TimelineId = timeline.Id,
            RevisionId = revision.Id,
            RequestedByUserId = caller.UserId,
            SettingsJson = request.Settings.GetRawText(),
            Status = RenderStatus.Queued,
            OutputBucketName = outputBucketName,
            OutputContentType = outputContentType,
            CreatedAt = now,
            UpdatedAt = now,
        };
        renderJob.OutputStorageKey = $"renders/timelines/{timeline.Id}/jobs/{renderJob.Id}.{format}";

        using var document = JsonDocument.Parse(revision.DocumentJson);
        var requested = new RenderingRequestedEvent(
            Guid.CreateVersion7(),
            RenderingRequestedEventType,
            now,
            renderJob.Id,
            timeline.Id,
            timeline.ProjectId,
            revision.Id,
            revision.RevisionNumber,
            caller.UserId,
            request.Settings.Clone(),
            document.RootElement.Clone(),
            mediaSources,
            outputBucketName,
            renderJob.OutputStorageKey,
            outputContentType);

        await _timelines.AddRenderJobAsync(renderJob, cancellationToken);
        await _timelines.EnqueueOutboxAsync(
            OutboxMessage.Create(
                $"rendering.requested:{renderJob.Id}",
                _rabbitMqOptions.ExchangeName,
                RenderingRequestedRoutingKey,
                RenderingRequestedEventType,
                requested),
            cancellationToken);
        await _timelines.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "VideoTimelineRenderQueued ProjectId={ProjectId} TimelineId={TimelineId} RevisionId={RevisionId} RevisionNumber={RevisionNumber} RenderJobId={RenderJobId}",
            timeline.ProjectId,
            timeline.Id,
            revision.Id,
            revision.RevisionNumber,
            renderJob.Id);

        return ToRenderJobDto(renderJob, revision.RevisionNumber);
    }

    public async Task<RenderJobDto> GetRenderJobAsync(
        Guid renderJobId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var renderJob = await _timelines.GetRenderJobByIdAsync(renderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        var timeline = await _timelines.GetByIdAsync(renderJob.TimelineId, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");
        await _projects.RequireReadAccessAsync(timeline.ProjectId, caller, cancellationToken);
        var revision = renderJob.RevisionId is null
            ? null
            : await _timelines.GetRevisionByIdAsync(renderJob.RevisionId.Value, cancellationToken);
        return ToRenderJobDto(renderJob, revision?.RevisionNumber);
    }

    public async Task RecordPerformanceAsync(
        Guid projectId,
        CallerContext caller,
        RecordVideoEditorPerformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.RequireReadAccessAsync(projectId, caller, cancellationToken);
        RequireVideoProject(project);
        ValidatePerformanceRequest(request);

        foreach (var metric in request.Metrics)
        {
            _logger.LogInformation(
                "VideoEditorPerformanceMetric ProjectId={ProjectId} MetricName={MetricName} DurationMs={DurationMs} TrackCount={TrackCount} ItemCount={ItemCount} RenderedItemCount={RenderedItemCount} TimelineDurationSeconds={TimelineDurationSeconds} MeasuredAt={MeasuredAt}",
                project.Id,
                metric.Name,
                metric.DurationMs,
                metric.TrackCount,
                metric.ItemCount,
                metric.RenderedItemCount,
                metric.TimelineDurationSeconds,
                metric.MeasuredAt);
        }
    }

    private static void RequireVideoProject(ProjectDocumentAccess project)
    {
        if (project.Kind != ProjectContentKind.Video)
        {
            throw DomainException.BadRequest("Video timelines are only available for video projects.");
        }
    }

    private static TimelineDocumentDto ToDocumentDto(Guid projectId, Guid timelineId, TimelineRevision revision)
    {
        using var document = JsonDocument.Parse(revision.DocumentJson);
        return new(
            projectId,
            timelineId,
            revision.Id,
            document.RootElement.Clone(),
            revision.RevisionNumber,
            revision.DocumentSchemaVersion,
            revision.Source,
            revision.Label,
            revision.CreatedAt,
            revision.CreatedByUserId);
    }

    private static RenderJobDto ToRenderJobDto(RenderJob renderJob, int? revisionNumber) =>
        new(
            renderJob.Id,
            renderJob.TimelineId,
            renderJob.RevisionId,
            revisionNumber,
            renderJob.Status.ToString().ToLowerInvariant(),
            renderJob.OutputBucketName,
            renderJob.OutputStorageKey,
            renderJob.OutputContentType,
            renderJob.OutputSizeBytes,
            null,
            renderJob.ErrorCode,
            renderJob.ErrorMessage,
            renderJob.StartedAt,
            renderJob.FinishedAt,
            renderJob.CreatedAt,
            renderJob.UpdatedAt);

    private async Task<IReadOnlyList<RenderingMediaSource>> ResolveReferencedMediaAsync(
        string documentJson,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(documentJson);
        var mediaIds = ExtractReferencedMediaIds(document.RootElement);
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var resolutions = await _media.ResolveAsync(mediaIds, caller, cancellationToken);
        var sources = new List<RenderingMediaSource>(resolutions.Count);
        foreach (var resolution in resolutions)
        {
            if (resolution.Availability != MediaResolutionAvailability.Available || resolution.Media is null)
            {
                throw DomainException.BadRequest("Render request contains media that is missing, inaccessible, deleted, failed, or still processing.");
            }

            var sourceKey = resolution.Media.CanonicalStorageKey
                ?? resolution.Media.ProxyStorageKey
                ?? resolution.Media.StorageKey;
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                throw DomainException.BadRequest("Render request contains media without a usable storage object.");
            }

            sources.Add(new RenderingMediaSource(
                resolution.MediaId,
                resolution.Media.Kind.ToString(),
                null,
                sourceKey,
                null,
                resolution.Media.SizeBytes,
                resolution.Media.DurationSeconds,
                resolution.Media.Width,
                resolution.Media.Height,
                resolution.Media.FrameRate,
                resolution.Media.Codec));
        }

        return sources;
    }

    private static IReadOnlyCollection<Guid> ExtractReferencedMediaIds(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            throw DomainException.BadRequest("Timeline document is not exportable.");
        }

        var documentMediaIds = new HashSet<string>(StringComparer.Ordinal);
        if (document.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in media.EnumerateObject())
            {
                documentMediaIds.Add(item.Name);
            }
        }

        var referenced = new HashSet<Guid>();
        if (!document.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
        {
            return referenced;
        }

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object
                || !track.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("mediaId", out var mediaIdElement)
                    || mediaIdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var mediaId = mediaIdElement.GetString();
                if (string.IsNullOrWhiteSpace(mediaId))
                {
                    throw DomainException.BadRequest("Timeline item mediaId must be a non-empty string.");
                }

                if (!documentMediaIds.Contains(mediaId))
                {
                    throw DomainException.BadRequest("Timeline item references media that is missing from the saved document.");
                }

                if (!Guid.TryParse(mediaId, out var parsedMediaId))
                {
                    throw DomainException.BadRequest("Timeline item mediaId must be a valid media id.");
                }

                referenced.Add(parsedMediaId);
            }
        }

        return referenced;
    }

    private static void ValidateSaveRequest(Guid projectId, SaveTimelineDocumentRequest request)
    {
        if (request.DocumentJson.ValueKind != JsonValueKind.Object)
        {
            throw DomainException.BadRequest("documentJson must be a JSON object.");
        }

        if (request.OperationsJson.ValueKind != JsonValueKind.Array)
        {
            throw DomainException.BadRequest("operationsJson must be a JSON array.");
        }

        if (request.DocumentSchemaVersion <= 0)
        {
            throw DomainException.BadRequest("documentSchemaVersion must be positive.");
        }

        if (!request.DocumentJson.TryGetProperty("projectId", out var documentProjectId)
            || documentProjectId.ValueKind != JsonValueKind.String
            || !Guid.TryParse(documentProjectId.GetString(), out var parsedProjectId)
            || parsedProjectId != projectId)
        {
            throw DomainException.BadRequest("documentJson.projectId must match the project id.");
        }

        if (!request.DocumentJson.TryGetProperty("schemaVersion", out var schemaVersion)
            || !schemaVersion.TryGetInt32(out var parsedSchemaVersion)
            || parsedSchemaVersion != request.DocumentSchemaVersion)
        {
            throw DomainException.BadRequest("documentJson.schemaVersion must match documentSchemaVersion.");
        }
    }

    private static void ValidateRenderSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw DomainException.BadRequest("settings must be a JSON object.");
        }

        var format = RequiredString(settings, "format");
        if (!SupportedRenderFormats.Contains(format))
        {
            throw DomainException.BadRequest("Render format must be mp4 or mov.");
        }

        var width = RequiredInt(settings, "width");
        var height = RequiredInt(settings, "height");
        if (width <= 0 || height <= 0)
        {
            throw DomainException.BadRequest("Render dimensions must be positive whole pixels.");
        }

        var frameRate = RequiredInt(settings, "frameRate");
        if (!SupportedRenderFrameRates.Contains(frameRate))
        {
            throw DomainException.BadRequest("Render frameRate must be 24, 25, 30, or 60.");
        }

        var quality = RequiredString(settings, "quality");
        if (!SupportedRenderQualities.Contains(quality))
        {
            throw DomainException.BadRequest("Render quality must be draft, standard, or high.");
        }
    }

    private static void ValidatePerformanceRequest(RecordVideoEditorPerformanceRequest request)
    {
        if (request.Metrics is null || request.Metrics.Count == 0)
        {
            throw DomainException.BadRequest("metrics must contain at least one item.");
        }

        if (request.Metrics.Count > 50)
        {
            throw DomainException.BadRequest("metrics cannot contain more than 50 items.");
        }

        foreach (var metric in request.Metrics)
        {
            if (!SupportedPerformanceMetricNames.Contains(metric.Name))
            {
                throw DomainException.BadRequest("Unsupported performance metric name.");
            }

            if (double.IsNaN(metric.DurationMs) || double.IsInfinity(metric.DurationMs) || metric.DurationMs < 0 || metric.DurationMs > 10 * 60 * 1000)
            {
                throw DomainException.BadRequest("durationMs must be a finite non-negative value.");
            }

            ValidateOptionalCount(metric.TrackCount, "trackCount");
            ValidateOptionalCount(metric.ItemCount, "itemCount");
            ValidateOptionalCount(metric.RenderedItemCount, "renderedItemCount");
            if (metric.TimelineDurationSeconds is { } timelineDurationSeconds
                && (double.IsNaN(timelineDurationSeconds)
                    || double.IsInfinity(timelineDurationSeconds)
                    || timelineDurationSeconds < 0
                    || timelineDurationSeconds > 24 * 60 * 60))
            {
                throw DomainException.BadRequest("timelineDurationSeconds must be finite and within one day.");
            }
        }
    }

    private static void ValidateOptionalCount(int? value, string name)
    {
        if (value is < 0 or > 1_000_000)
        {
            throw DomainException.BadRequest($"{name} must be between 0 and 1000000.");
        }
    }

    private static int OperationCount(JsonElement operations)
    {
        return operations.ValueKind == JsonValueKind.Array ? operations.GetArrayLength() : 0;
    }

    private static string[] OperationIds(JsonElement operations)
    {
        if (operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return operations.EnumerateArray()
            .SelectMany(operation =>
            {
                var ids = new List<string>();
                if (operation.ValueKind != JsonValueKind.Object)
                {
                    return ids;
                }

                if (operation.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    ids.Add(id.GetString()!);
                }

                if (operation.TryGetProperty("operationIds", out var operationIds) && operationIds.ValueKind == JsonValueKind.Array)
                {
                    ids.AddRange(operationIds.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        .Select(item => item.GetString()!));
                }

                return ids;
            })
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw DomainException.BadRequest($"settings.{propertyName} is required.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static int RequiredInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
        {
            throw DomainException.BadRequest($"settings.{propertyName} is required.");
        }

        return value;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

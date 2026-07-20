using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
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
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Timelines.Services;

internal sealed class TimelineService(
    ITimelineRepository timelines,
    IProjectsApi projects,
    IMediaApi media,
    IFileStorageService storage,
    IRenderRealtimeNotifier realtime,
    EditorDocumentCache documentCache,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<TimelineService> logger) : ITimelineService
{
    private readonly ITimelineRepository _timelines = timelines;
    private readonly IProjectsApi _projects = projects;
    private readonly IMediaApi _media = media;
    private readonly IFileStorageService _storage = storage;
    private readonly IRenderRealtimeNotifier _realtime = realtime;
    private readonly EditorDocumentCache _documentCache = documentCache;
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

        if (!_documentCache.DocumentsEnabled)
        {
            var timeline = await _timelines.GetByProjectAsync(project.Id, cancellationToken)
                ?? throw DomainException.NotFound("Video timeline not found.");
            var latestRevision = await _timelines.GetLatestRevisionAsync(timeline.Id, cancellationToken)
                ?? throw DomainException.NotFound("Video timeline not found.");

            return LogAndConvert(project.Id, timeline.Id, latestRevision);
        }

        var identity = await _timelines.GetCurrentRevisionIdentityAsync(project.Id, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");
        return await _documentCache.GetTimelineDocumentAsync(
            project.Id,
            identity.RevisionNumber,
            async ct =>
            {
                var revision = await _timelines.GetRevisionByIdAsync(identity.RevisionId, ct)
                    ?? throw DomainException.NotFound("Video timeline not found.");
                return LogAndConvert(project.Id, identity.TimelineId, revision);
            },
            cancellationToken);

        TimelineDocumentDto LogAndConvert(Guid currentProjectId, Guid timelineId, TimelineRevision revision)
        {
            _logger.LogInformation(
                "VideoTimelineGet ProjectId={ProjectId} TimelineId={TimelineId} RevisionId={RevisionId} RevisionNumber={RevisionNumber}",
                currentProjectId,
                timelineId,
                revision.Id,
                revision.RevisionNumber);
            return ToDocumentDto(currentProjectId, timelineId, revision);
        }
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

        var result = ToDocumentDto(project.Id, timeline.Id, revision);
        await _documentCache.WriteTimelineDocumentAsync(project.Id, revision.RevisionNumber, result);
        await _documentCache.WriteTimelineListAsync(
            project.Id,
            revision.RevisionNumber,
            (IReadOnlyList<TimelineDto>)[ToTimelineDto(timeline)]);
        return result;
    }

    public async Task<IReadOnlyList<TimelineDto>> ListByProjectAsync(
        Guid projectId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.RequireReadAccessAsync(projectId, caller, cancellationToken);
        RequireVideoProject(project);

        async Task<IReadOnlyList<TimelineDto>> Load(CancellationToken ct) =>
            (await _timelines.ListByProjectAsync(project.Id, ct)).Select(ToTimelineDto).ToList();

        if (!_documentCache.DocumentsEnabled)
        {
            return await Load(cancellationToken);
        }

        var identity = await _timelines.GetCurrentRevisionIdentityAsync(project.Id, cancellationToken);
        return identity is null
            ? []
            : await _documentCache.GetTimelineListAsync(
                project.Id,
                identity.RevisionNumber,
                Load,
                cancellationToken);
    }

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

        var revision = await _timelines.GetRevisionByNumberAsync(timeline.Id, request.RevisionNumber, cancellationToken)
            ?? throw DomainException.NotFound("Timeline revision not found.");

        var now = DateTimeOffset.UtcNow;
        var mediaSources = await ResolveReferencedMediaAsync(revision.DocumentJson, caller, cancellationToken);
        var format = request.Settings.GetProperty("format").GetString() ?? "mp4";
        var outputContentType = format == "mov"
            ? "video/quicktime"
            : "video/mp4";
        var outputBucketName = _storageOptions.RenderBucketName;
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
        var result = ToRenderJobDto(renderJob, revision.RevisionNumber);
        await _documentCache.WriteRenderJobAsync(
            renderJob.Id,
            renderJob.Status.ToString().ToLowerInvariant(),
            renderJob.UpdatedAt,
            result);
        await _realtime.RenderJobUpdatedAsync(renderJob, cancellationToken);

        _logger.LogInformation(
            "VideoTimelineRenderQueued ProjectId={ProjectId} TimelineId={TimelineId} RevisionId={RevisionId} RevisionNumber={RevisionNumber} RenderJobId={RenderJobId}",
            timeline.ProjectId,
            timeline.Id,
            revision.Id,
            revision.RevisionNumber,
            renderJob.Id);

        return result;
    }

    public async Task<RenderJobDto> GetRenderJobAsync(
        Guid renderJobId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        if (!_documentCache.RenderJobsEnabled)
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

        var state = await _timelines.GetRenderJobAccessStateAsync(renderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        await _projects.RequireReadAccessAsync(state.ProjectId, caller, cancellationToken);
        return await _documentCache.GetRenderJobAsync(
            renderJobId,
            state.Status.ToString().ToLowerInvariant(),
            state.UpdatedAt,
            async ct =>
            {
                var renderJob = await _timelines.GetRenderJobByIdAsync(renderJobId, ct)
                    ?? throw DomainException.NotFound("Render job not found.");
                return ToRenderJobDto(renderJob, state.RevisionNumber);
            },
            cancellationToken);
    }

    public async Task<RenderJobOutputDownload> GetRenderJobOutputAsync(
        Guid renderJobId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var renderJob = await _timelines.GetRenderJobByIdAsync(renderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        var timeline = await _timelines.GetByIdAsync(renderJob.TimelineId, cancellationToken)
            ?? throw DomainException.NotFound("Video timeline not found.");
        await _projects.RequireReadAccessAsync(timeline.ProjectId, caller, cancellationToken);

        if (renderJob.Status != RenderStatus.Completed)
        {
            throw DomainException.Conflict("Render output is only available after the job completes.");
        }

        if (string.IsNullOrWhiteSpace(renderJob.OutputBucketName)
            || string.IsNullOrWhiteSpace(renderJob.OutputStorageKey))
        {
            throw DomainException.NotFound("Render output not found.");
        }

        var downloaded = await _storage.DownloadAsync(
            renderJob.OutputBucketName,
            renderJob.OutputStorageKey,
            cancellationToken);

        var contentType = string.IsNullOrWhiteSpace(downloaded.ContentType)
            ? renderJob.OutputContentType ?? ContentTypeForRenderOutput(renderJob.OutputStorageKey)
            : downloaded.ContentType;

        return new RenderJobOutputDownload(
            downloaded.Stream,
            contentType,
            downloaded.ContentLength,
            downloaded.ETag,
            DownloadFileName(renderJob.OutputStorageKey));
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

    private static TimelineDto ToTimelineDto(Timeline timeline) =>
        new(timeline.Id, timeline.ProjectId, timeline.Name, timeline.CreatedAt, timeline.UpdatedAt);

    private static RenderJobDto ToRenderJobDto(RenderJob renderJob, int? revisionNumber)
    {
        var realtime = RenderRealtimeUpdate.FromJob(renderJob);
        return new(
            renderJob.Id,
            renderJob.TimelineId,
            renderJob.RevisionId,
            revisionNumber,
            renderJob.Status.ToString().ToLowerInvariant(),
            realtime.OutputAvailable,
            renderJob.OutputContentType,
            renderJob.OutputSizeBytes,
            renderJob.ErrorCode,
            renderJob.ErrorMessage,
            realtime.Message,
            renderJob.StartedAt,
            renderJob.FinishedAt,
            renderJob.CreatedAt,
            renderJob.UpdatedAt);
    }

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

            var sourceObject = PreferredRenderSource(resolution.Media);
            if (sourceObject is null)
            {
                throw DomainException.BadRequest("Render request contains media without a usable storage object.");
            }

            sources.Add(new RenderingMediaSource(
                resolution.MediaId,
                resolution.Media.Kind.ToString(),
                sourceObject.Value.BucketName,
                sourceObject.Value.ObjectKey,
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

    private static (string BucketName, string ObjectKey)? PreferredRenderSource(MediaSummary media)
    {
        if (media.CanonicalBucketName is { Length: > 0 } canonicalBucket
            && media.CanonicalStorageKey is { Length: > 0 } canonicalKey)
        {
            return (canonicalBucket, canonicalKey);
        }

        if (media.ProxyBucketName is { Length: > 0 } proxyBucket
            && media.ProxyStorageKey is { Length: > 0 } proxyKey)
        {
            return (proxyBucket, proxyKey);
        }

        if (media.RawBucketName is { Length: > 0 } rawBucket
            && media.RawStorageKey is { Length: > 0 } rawKey)
        {
            return (rawBucket, rawKey);
        }

        if (media.RawBucketName is { Length: > 0 } legacyRawBucket
            && media.StorageKey is { Length: > 0 } legacyRawKey)
        {
            return (legacyRawBucket, legacyRawKey);
        }

        return null;
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

    private static string ContentTypeForRenderOutput(string objectKey) =>
        Path.GetExtension(objectKey).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };

    private static string DownloadFileName(string objectKey)
    {
        var extension = Path.GetExtension(objectKey);
        return string.IsNullOrWhiteSpace(extension)
            ? "kuvox-render"
            : $"kuvox-render{extension}";
    }
}

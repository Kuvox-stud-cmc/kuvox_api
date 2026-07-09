using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Microsoft.Extensions.Options;
using MediaEntity = Kuvox.Api.Modules.Media.Models.Media;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class MediaPipelineRecoveryService(
    IServiceScopeFactory scopeFactory,
    IOptions<MediaPipelineRecoveryOptions> options,
    ILogger<MediaPipelineRecoveryService> logger)
    : BackgroundService
{
    private readonly MediaPipelineRecoveryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("[Media] Media pipeline recovery is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStaleMediaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Media] Media pipeline recovery loop failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                stoppingToken);
        }
    }

    private async Task RecoverStaleMediaAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var realtime = scope.ServiceProvider.GetRequiredService<IMediaRealtimeNotifier>();
        var rabbitMqOptions = scope.ServiceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(Math.Max(1, _options.StaleAfterMinutes));
        var batchSize = Math.Max(1, _options.BatchSize);
        var staleItems = await repository.ListStalePipelineAsync(cutoff, batchSize, cancellationToken);
        if (staleItems.Count == 0)
        {
            return;
        }

        var realtimeUpdates = new List<(MediaEntity Media, string Phase, string? ErrorCode, string? ErrorMessage)>();

        foreach (var item in staleItems)
        {
            switch (item.Status)
            {
                case MediaStatus.Uploaded:
                    await RecoverUploadedAsync(repository, rabbitMqOptions, item, now, realtimeUpdates, cancellationToken);
                    break;
                case MediaStatus.Processing:
                    await RecoverProcessingAsync(repository, rabbitMqOptions, item, now, realtimeUpdates, cancellationToken);
                    break;
            }
        }

        await repository.SaveChangesAsync(cancellationToken);

        foreach (var update in realtimeUpdates)
        {
            await realtime.MediaUpdatedAsync(
                update.Media,
                MediaService.ToDto(update.Media),
                update.Phase,
                cancellationToken,
                errorCode: update.ErrorCode,
                errorMessage: update.ErrorMessage);
        }
    }

    private static async Task RecoverUploadedAsync(
        IMediaRepository repository,
        RabbitMqOptions rabbitMqOptions,
        MediaEntity item,
        DateTimeOffset now,
        List<(MediaEntity Media, string Phase, string? ErrorCode, string? ErrorMessage)> realtimeUpdates,
        CancellationToken cancellationToken)
    {
        if (!HasStorageObject(item.RawBucketName, item.RawStorageKey))
        {
            if (HasStorageObject(item.CanonicalBucketName, item.CanonicalStorageKey))
            {
                item.Status = MediaStatus.Processing;
                realtimeUpdates.Add((item, "processing", null, null));
                await RecoverProcessingAsync(repository, rabbitMqOptions, item, now, realtimeUpdates, cancellationToken);
                return;
            }

            MarkFailed(
                item,
                now,
                "Media pipeline recovery could not resume optimization because raw and canonical storage metadata are missing.",
                realtimeUpdates);
            return;
        }

        var requested = new MediaOptimizationRequestedEvent(
            EventId: Guid.NewGuid(),
            EventType: "media.optimization.requested",
            OccurredAt: now,
            MediaId: item.Id,
            UserId: item.OwnerId,
            BucketName: item.RawBucketName!,
            ObjectKey: item.RawStorageKey!,
            ContentType: InferContentType(item.Filename, item.Kind),
            OriginalFileName: item.Filename,
            SizeBytes: item.RawSizeBytes ?? item.SizeBytes,
            Kind: item.Kind);

        await repository.EnsurePendingOutboxAsync(
            OutboxMessage.Create(
                dedupeKey: $"media.optimization.requested:{item.Id}",
                exchange: rabbitMqOptions.ExchangeName,
                routingKey: "media.optimization.requested",
                eventType: requested.EventType,
                payload: requested),
            cancellationToken);

        item.UpdatedAt = now;
    }

    private static async Task RecoverProcessingAsync(
        IMediaRepository repository,
        RabbitMqOptions rabbitMqOptions,
        MediaEntity item,
        DateTimeOffset now,
        List<(MediaEntity Media, string Phase, string? ErrorCode, string? ErrorMessage)> realtimeUpdates,
        CancellationToken cancellationToken)
    {
        if (!HasStorageObject(item.CanonicalBucketName, item.CanonicalStorageKey))
        {
            MarkFailed(
                item,
                now,
                "Media pipeline recovery could not resume ingestion because canonical storage metadata is missing.",
                realtimeUpdates);
            return;
        }

        item.StorageKey = item.CanonicalStorageKey!;
        if (item.CanonicalSizeBytes is { } canonicalSizeBytes)
        {
            item.SizeBytes = canonicalSizeBytes;
        }

        var canonical = OptimizedObjectFrom(item.CanonicalBucketName, item.CanonicalStorageKey, item.CanonicalSizeBytes ?? item.SizeBytes, item.Kind)!;
        var requested = new IngestionRequestedEvent(
            EventId: Guid.NewGuid(),
            EventType: "ingestion.requested",
            OccurredAt: now,
            MediaId: item.Id,
            OwnerId: item.OwnerId,
            OwnerKind: item.OwnerKind,
            Kind: item.Kind,
            Canonical: canonical,
            Proxy: OptimizedObjectFrom(item.ProxyBucketName, item.ProxyStorageKey, item.ProxySizeBytes, item.Kind),
            Thumbnail: OptimizedObjectFrom(item.ThumbnailBucketName, item.ThumbnailStorageKey, item.ThumbnailSizeBytes, MediaKind.Image),
            DurationSeconds: item switch
            {
                Models.Video video => video.DurationSeconds,
                Models.Audio audio => audio.DurationSeconds,
                _ => null
            },
            Width: item switch
            {
                Models.Video video => video.Width,
                Models.Photo photo => photo.Width,
                _ => null
            },
            Height: item switch
            {
                Models.Video video => video.Height,
                Models.Photo photo => photo.Height,
                _ => null
            },
            FrameRate: item is Models.Video frameRateSource ? frameRateSource.FrameRate : null,
            Codec: item.Codec);

        await repository.EnsurePendingOutboxAsync(
            OutboxMessage.Create(
                dedupeKey: $"ingestion.requested:{item.Id}",
                exchange: rabbitMqOptions.ExchangeName,
                routingKey: "ingestion.requested",
                eventType: requested.EventType,
                payload: requested),
            cancellationToken);

        item.UpdatedAt = now;
    }

    private static void MarkFailed(
        MediaEntity item,
        DateTimeOffset now,
        string errorMessage,
        List<(MediaEntity Media, string Phase, string? ErrorCode, string? ErrorMessage)> realtimeUpdates)
    {
        item.Status = MediaStatus.Failed;
        item.ErrorMessage = errorMessage;
        item.UpdatedAt = now;
        realtimeUpdates.Add((item, "failed", "media_pipeline_recovery_failed", errorMessage));
    }

    private static OptimizedMediaObject? OptimizedObjectFrom(
        string? bucketName,
        string? objectKey,
        long? sizeBytes,
        MediaKind kind)
    {
        if (!HasStorageObject(bucketName, objectKey))
        {
            return null;
        }

        return new OptimizedMediaObject(
            bucketName!,
            objectKey!,
            InferContentType(objectKey!, kind),
            sizeBytes ?? 0);
    }

    private static bool HasStorageObject(string? bucketName, string? objectKey) =>
        !string.IsNullOrWhiteSpace(bucketName) && !string.IsNullOrWhiteSpace(objectKey);

    private static string InferContentType(string filenameOrKey, MediaKind kind)
    {
        var extension = Path.GetExtension(filenameOrKey).ToLowerInvariant();
        if (extension.Length > 0 && ContentTypes.TryGetValue(extension, out var contentType))
        {
            return contentType;
        }

        return kind switch
        {
            MediaKind.Video => "video/mp4",
            MediaKind.Audio => "audio/mpeg",
            MediaKind.Image => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>
    {
        [".aac"] = "audio/aac",
        [".aiff"] = "audio/aiff",
        [".avi"] = "video/x-msvideo",
        [".flac"] = "audio/flac",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".m4a"] = "audio/mp4",
        [".mkv"] = "video/x-matroska",
        [".mov"] = "video/quicktime",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".png"] = "image/png",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".wav"] = "audio/wav",
        [".webm"] = "video/webm",
        [".webp"] = "image/webp"
    };
}

using System.Reflection;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Repositories.Migrations;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class MediaSearchRevisionTests
{
    [Fact]
    public void Model_configures_bigint_revision_with_zero_default_for_all_tpc_media()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new MediaDbContext(options);

        var property = context.Model.FindEntityType(typeof(Media))!
            .FindProperty(nameof(Media.SearchRevision));

        Assert.NotNull(property);
        Assert.Equal(typeof(long), property!.ClrType);
        Assert.Equal(0L, property.GetDefaultValue());
    }

    [Fact]
    public void Migration_adds_all_tpc_columns_and_backfills_ready_rows()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new ExposedMigration().Apply(builder);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(3, columns.Count);
        Assert.All(columns, column =>
        {
            Assert.Equal("SearchRevision", column.Name);
            Assert.Equal("bigint", column.ColumnType);
            Assert.Equal(0L, column.DefaultValue);
        });
        var backfills = builder.Operations.OfType<SqlOperation>().ToList();
        Assert.Equal(3, backfills.Count);
        Assert.All(backfills, operation =>
        {
            Assert.Contains("\"Status\" = 'Ready'", operation.Sql, StringComparison.Ordinal);
            Assert.Contains("\"SearchRevision\" = 1", operation.Sql, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Accepted_ingestion_is_idempotent_and_reingestion_advances_revision()
    {
        var item = VideoWithRevision(1, MediaStatus.Processing);
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var service = CreateService(repository);
        var completed = Completed(item.Id);

        await service.HandleIngestionCompletedAsync(completed);
        await service.HandleIngestionCompletedAsync(completed);
        Assert.Equal(2, item.SearchRevision);
        Assert.Equal(1, handler.SaveCalls);

        item.Status = MediaStatus.Processing;
        await service.HandleIngestionCompletedAsync(completed with { EventId = Guid.NewGuid() });
        Assert.Equal(3, item.SearchRevision);
        Assert.Equal(2, handler.SaveCalls);
    }

    [Fact]
    public async Task Disabled_ingestion_marks_optimized_media_ready_without_outbox_work()
    {
        var item = VideoWithRevision(0, MediaStatus.Uploaded);
        item.ErrorMessage = "old error";
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var service = CreateService(repository, ingestionEnabled: false);

        await service.HandleOptimizationCompletedAsync(OptimizationCompleted(item.Id));

        Assert.Equal(MediaStatus.Ready, item.Status);
        Assert.Equal(0, item.SearchRevision);
        Assert.Null(item.ErrorMessage);
        Assert.Equal("canonical/media.mp4", item.CanonicalStorageKey);
        Assert.Empty(handler.EnqueuedOutbox);
        Assert.Equal(1, handler.SaveCalls);
    }

    [Fact]
    public async Task Enabled_ingestion_keeps_optimization_completion_compatible()
    {
        var item = VideoWithRevision(0, MediaStatus.Uploaded);
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var service = CreateService(repository, ingestionEnabled: true);

        await service.HandleOptimizationCompletedAsync(OptimizationCompleted(item.Id));

        Assert.Equal(MediaStatus.Processing, item.Status);
        var message = Assert.Single(handler.EnqueuedOutbox);
        Assert.Equal("ingestion.requested", message.RoutingKey);
    }

    [Fact]
    public async Task Disabled_ingestion_ignores_late_success_and_failure_results()
    {
        var item = VideoWithRevision(0, MediaStatus.Ready);
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var service = CreateService(repository, ingestionEnabled: false);

        await service.HandleIngestionCompletedAsync(Completed(item.Id));
        await service.HandleIngestionFailedAsync(new IngestionFailedEvent(
            Guid.NewGuid(),
            "ingestion.failed",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            item.Id,
            "late_failure",
            "Late failure"));

        Assert.Equal(MediaStatus.Ready, item.Status);
        Assert.Equal(0, item.SearchRevision);
        Assert.Equal(0, handler.SaveCalls);
        Assert.Equal(0, handler.GetByIdCalls);
    }

    [Theory]
    [InlineData(MediaStatus.Processing)]
    [InlineData(MediaStatus.Failed)]
    public async Task Disabled_recovery_promotes_storage_verified_optimized_media(MediaStatus status)
    {
        var item = VideoWithRevision(0, status);
        item.CanonicalBucketName = "canonical";
        item.CanonicalStorageKey = "canonical/media.mp4";
        item.CanonicalSizeBytes = 42;
        item.ErrorMessage = "ingestion failed";
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var updates = new List<(Media Media, string Phase, string? ErrorCode, string? ErrorMessage)>();

        await MediaPipelineRecoveryService.RecoverProcessingAsync(
            repository,
            StorageProxy.Create(exists: true),
            new RabbitMqOptions(),
            item,
            DateTimeOffset.UtcNow,
            ingestionEnabled: false,
            updates,
            CancellationToken.None);

        Assert.Equal(MediaStatus.Ready, item.Status);
        Assert.Equal(0, item.SearchRevision);
        Assert.Null(item.ErrorMessage);
        Assert.Equal("canonical/media.mp4", item.StorageKey);
        Assert.Empty(handler.EnsuredOutbox);
        Assert.Equal("ready", Assert.Single(updates).Phase);
    }

    [Fact]
    public async Task Enabled_recovery_backfills_ready_revision_zero_without_hiding_preview()
    {
        var item = VideoWithRevision(0, MediaStatus.Ready);
        item.CanonicalBucketName = "canonical";
        item.CanonicalStorageKey = "canonical/media.mp4";
        item.CanonicalSizeBytes = 42;
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var updates = new List<(Media Media, string Phase, string? ErrorCode, string? ErrorMessage)>();

        await MediaPipelineRecoveryService.RecoverProcessingAsync(
            repository,
            StorageProxy.Create(exists: true),
            new RabbitMqOptions(),
            item,
            DateTimeOffset.UtcNow,
            ingestionEnabled: true,
            updates,
            CancellationToken.None);

        Assert.Equal(MediaStatus.Ready, item.Status);
        Assert.Equal(0, item.SearchRevision);
        Assert.Empty(updates);
        Assert.Equal("ingestion.requested", Assert.Single(handler.EnsuredOutbox).RoutingKey);

        await CreateService(repository, ingestionEnabled: true).HandleIngestionCompletedAsync(Completed(item.Id));
        Assert.Equal(MediaStatus.Ready, item.Status);
        Assert.Equal(1, item.SearchRevision);
    }

    [Fact]
    public async Task Recovery_requires_the_canonical_storage_object_before_promotion()
    {
        var item = VideoWithRevision(0, MediaStatus.Processing);
        item.CanonicalBucketName = "canonical";
        item.CanonicalStorageKey = "canonical/missing.mp4";
        var (repository, handler) = MediaRepositoryProxy.Create(item);
        var updates = new List<(Media Media, string Phase, string? ErrorCode, string? ErrorMessage)>();

        await MediaPipelineRecoveryService.RecoverProcessingAsync(
            repository,
            StorageProxy.Create(exists: false),
            new RabbitMqOptions(),
            item,
            DateTimeOffset.UtcNow,
            ingestionEnabled: false,
            updates,
            CancellationToken.None);

        Assert.Equal(MediaStatus.Failed, item.Status);
        Assert.Empty(handler.EnsuredOutbox);
        Assert.Equal("failed", Assert.Single(updates).Phase);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    public void Ingestion_flag_requires_explicit_true(string? value, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(value is null
                ? []
                : new Dictionary<string, string?> { ["KUVOX_MEDIA_INGESTION_ENABLED"] = value })
            .Build();

        Assert.Equal(expected, MediaFeatureOptions.FromConfiguration(configuration).IngestionEnabled);
    }

    [Fact]
    public async Task Media_summary_propagates_search_revision()
    {
        var item = VideoWithRevision(17, MediaStatus.Ready);
        var (repository, _) = MediaRepositoryProxy.Create(item);
        var api = new MediaApi(repository, Noop<IAlbumRepository>());

        var summary = await api.GetSummaryAsync(item.Id);

        Assert.NotNull(summary);
        Assert.Equal(17, summary.SearchRevision);
    }

    private static Video VideoWithRevision(long revision, MediaStatus status) =>
        new()
        {
            OwnerId = Guid.NewGuid(),
            OwnerKind = OwnerKind.User,
            Filename = "revision.mp4",
            StorageKey = "raw/revision.mp4",
            Status = status,
            SearchRevision = revision,
            DurationSeconds = 1,
            Width = 640,
            Height = 360,
            FrameRate = 30,
        };

    private static MediaService CreateService(
        IMediaRepository repository,
        bool ingestionEnabled = true,
        IFileStorageService? storage = null)
    {
        var options = new CachingOptions();
        var store = new DisabledCacheStore();
        var keys = new CacheKeyFactory(options);
        var codec = new JsonCacheCodec(new SystemCacheClock());
        var cache = new BusinessCache(
            store,
            codec,
            Options.Create(options),
            NullLogger<BusinessCache>.Instance);
        var generations = new CacheGenerationManager(
            store,
            keys,
            codec,
            Options.Create(options),
            NullLogger<CacheGenerationManager>.Instance);
        return new MediaService(
            repository,
            Noop<IAlbumRepository>(),
            Noop<IAuthApi>(),
            Noop<INotificationsApi>(),
            Noop<IMediator>(),
            storage ?? Noop<IFileStorageService>(),
            Noop<IMediaRealtimeNotifier>(),
            Options.Create(new RabbitMqOptions()),
            NullLogger<MediaService>.Instance,
            cache,
            generations,
            keys,
            Options.Create(options),
            Options.Create(new MediaFeatureOptions { IngestionEnabled = ingestionEnabled }));
    }

    private static MediaOptimizationCompletedEvent OptimizationCompleted(Guid mediaId) =>
        new(
            Guid.NewGuid(),
            "media.optimization.completed",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            mediaId,
            new OptimizedMediaObject("canonical", "canonical/media.mp4", "video/mp4", 42),
            new OptimizedMediaObject("proxy", "proxy/media.mp4", "video/mp4", 21),
            new OptimizedMediaObject("thumbnails", "thumbnails/media.webp", "image/webp", 4),
            1,
            640,
            360,
            30,
            "h264",
            "raw",
            "raw/media.mp4",
            84);

    private static IngestionCompletedEvent Completed(Guid mediaId) =>
        new(
            Guid.NewGuid(),
            "ingestion.completed",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            mediaId,
            ShotCount: 1,
            TranscriptCount: 1,
            OcrCount: 1);

    private static T Noop<T>() where T : class => DispatchProxy.Create<T, NoopProxy>();

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var valueType = returnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(valueType)
                    .Invoke(null, [valueType.IsValueType ? Activator.CreateInstance(valueType) : null]);
            }
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class MediaRepositoryProxy : DispatchProxy
    {
        private Media? _item;
        public int SaveCalls { get; private set; }
        public int GetByIdCalls { get; private set; }
        public List<OutboxMessage> EnqueuedOutbox { get; } = [];
        public List<OutboxMessage> EnsuredOutbox { get; } = [];

        public static (IMediaRepository Repository, MediaRepositoryProxy Handler) Create(Media item)
        {
            var repository = DispatchProxy.Create<IMediaRepository, MediaRepositoryProxy>();
            var handler = (MediaRepositoryProxy)(object)repository;
            handler._item = item;
            return (repository, handler);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IMediaRepository.GetByIdAsync) => GetById(),
                nameof(IMediaRepository.SaveChangesAsync) => Save(),
                nameof(IMediaRepository.EnqueueOutboxAsync) => Capture((OutboxMessage)args![0]!, EnqueuedOutbox),
                nameof(IMediaRepository.EnsurePendingOutboxAsync) => Capture((OutboxMessage)args![0]!, EnsuredOutbox),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private Task<Media?> GetById()
        {
            GetByIdCalls++;
            return Task.FromResult(_item);
        }

        private static Task Capture(OutboxMessage message, List<OutboxMessage> destination)
        {
            destination.Add(message);
            return Task.CompletedTask;
        }

        private Task Save()
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private class StorageProxy : DispatchProxy
    {
        private bool _exists;

        public static IFileStorageService Create(bool exists)
        {
            var storage = Create<IFileStorageService, StorageProxy>();
            ((StorageProxy)(object)storage)._exists = exists;
            return storage;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IFileStorageService.ExistsAsync) => Task.FromResult(_exists),
                nameof(IFileStorageService.DeleteAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    private sealed class ExposedMigration : AddMediaSearchRevision
    {
        public void Apply(MigrationBuilder builder) => base.Up(builder);
    }
}

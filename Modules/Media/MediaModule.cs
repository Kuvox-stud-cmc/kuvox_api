using Amazon.Runtime;
using Amazon.S3;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema)));

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddOptions<MediaPipelineRecoveryOptions>()
            .Bind(configuration.GetSection(MediaPipelineRecoveryOptions.SectionName))
            .Validate(options => options.StaleAfterMinutes > 0, "MediaPipelineRecovery:StaleAfterMinutes must be positive.")
            .Validate(options => options.PollIntervalSeconds > 0, "MediaPipelineRecovery:PollIntervalSeconds must be positive.")
            .Validate(options => options.BatchSize > 0, "MediaPipelineRecovery:BatchSize must be positive.")
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
            var s3Config = new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = options.Region
            };

            return new AmazonS3Client(credentials, s3Config);
        });

        services.AddScoped<IFileStorageService, SeaweedFileStorageService>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IAlbumService, AlbumService>();
        services.AddScoped<IMediaApi, MediaApi>();
        services.AddScoped<IMediaRealtimeNotifier, MediaRealtimeNotifier>();
        services.AddHostedService<MediaOptimizationResultConsumer>();
        services.AddHostedService<IngestionResultConsumer>();
        services.AddHostedService<MediaPipelineRecoveryService>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}

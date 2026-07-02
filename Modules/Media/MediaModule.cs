using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Services;
using Microsoft.EntityFrameworkCore;
using Amazon.Runtime;
using Amazon.S3;

namespace Kuvox.Api.Modules.Media;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema)));

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var storage = configuration.GetSection("Storage");

            var endpoint = storage["Endpoint"]
                ?? throw new InvalidOperationException("Storage:Endpoint is missing.");

            var accessKey = storage["AccessKey"]
                ?? throw new InvalidOperationException("Storage:AccessKey is missing.");

            var secretKey = storage["SecretKey"]
                ?? throw new InvalidOperationException("Storage:SecretKey is missing.");

            var region = storage["Region"] ?? "us-east-1";

            var credentials = new BasicAWSCredentials(accessKey, secretKey);

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = region
            };

            return new AmazonS3Client(credentials, s3Config);
        });

        services.AddScoped<IFileStorageService, SeaweedFileStorageService>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IAlbumService, AlbumService>();
        services.AddScoped<IMediaApi, MediaApi>();

        // INotificationHandler<ProjectDeletedEvent> is discovered by the MediatR assembly scan.
        return services;
    }
}

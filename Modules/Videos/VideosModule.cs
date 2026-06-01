using Kuvox.Api.Modules.Videos.Contracts;
using Kuvox.Api.Modules.Videos.Repositories;
using Kuvox.Api.Modules.Videos.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Videos;

public static class VideosModule
{
    public static IServiceCollection AddVideosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<VideosDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", VideosDbContext.Schema)));

        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<IVideoService, VideoService>();
        services.AddScoped<IVideosApi, VideosApi>();

        // INotificationHandler<ProjectDeletedEvent> is discovered by the MediatR assembly scan.
        return services;
    }
}

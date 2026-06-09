using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Media;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema)));

        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IMediaApi, MediaApi>();

        // INotificationHandler<ProjectDeletedEvent> is discovered by the MediatR assembly scan.
        return services;
    }
}

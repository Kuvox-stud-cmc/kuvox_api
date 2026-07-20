using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Notifications.Services;
using Kuvox.Api.Modules.Shared.Infrastructure.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Notifications;

public static class NotificationsModule
{

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema));
            options.AddInterceptors(sp.GetRequiredService<DatabaseCommandMetricsInterceptor>());
        });

        services.AddScoped<INotificationsRepository, NotificationsRepository>();
        services.AddSingleton<NotificationCacheInvalidator>();
        services.AddScoped<INotificationsService, NotificationsService>();
        services.AddScoped<INotificationsApi, NotificationsApi>();

        return services;
    }

}

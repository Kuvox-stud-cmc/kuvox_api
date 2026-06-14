using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Notifications;

public static class NotificationsModule
{

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(options => 
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));

        services.AddScoped<INotificationsRepository, NotificationsRepository>();
        services.AddScoped<INotificationsService, NotificationsService>();
        services.AddScoped<INotificationsApi, NotificationsApi>();

        return services;
    }

}

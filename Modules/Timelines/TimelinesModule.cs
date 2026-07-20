using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Timelines.Services;
using Kuvox.Api.Modules.Shared.Infrastructure.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Timelines;

public static class TimelinesModule
{
    public static IServiceCollection AddTimelinesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TimelinesDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TimelinesDbContext.Schema));
            options.AddInterceptors(sp.GetRequiredService<DatabaseCommandMetricsInterceptor>());
        });

        services.AddScoped<ITimelineRepository, TimelineRepository>();
        services.AddScoped<ITimelineService, TimelineService>();
        services.AddScoped<ITimelinesApi, TimelinesApi>();
        services.AddScoped<IRenderRealtimeNotifier, RenderRealtimeNotifier>();
        services.AddHostedService<RenderingResultConsumer>();

        return services;
    }
}

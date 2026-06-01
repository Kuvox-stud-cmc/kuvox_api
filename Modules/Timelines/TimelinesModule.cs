using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Timelines.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Timelines;

public static class TimelinesModule
{
    public static IServiceCollection AddTimelinesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TimelinesDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TimelinesDbContext.Schema)));

        services.AddScoped<ITimelineRepository, TimelineRepository>();
        services.AddScoped<ITimelineService, TimelineService>();
        services.AddScoped<ITimelinesApi, TimelinesApi>();

        return services;
    }
}

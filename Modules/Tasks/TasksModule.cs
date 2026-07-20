using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Repositories;
using Kuvox.Api.Modules.Tasks.Services;
using Kuvox.Api.Modules.Shared.Infrastructure.Metrics;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Tasks;

internal static class TasksModule
{
    internal static IServiceCollection AddTasksModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TasksDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TasksDbContext.Schema));
            options.AddInterceptors(sp.GetRequiredService<DatabaseCommandMetricsInterceptor>());
        });

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ICachePrewarmTarget, TaskCachePrewarmTarget>();
        services.AddScoped<ITasksApi, TasksApi>();

        return services;
    }
}

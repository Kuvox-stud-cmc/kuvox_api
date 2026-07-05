using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Repositories;
using Kuvox.Api.Modules.Tasks.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Tasks;

internal static class TasksModule
{
    internal static IServiceCollection AddTasksModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TasksDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TasksDbContext.Schema)));

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITasksApi, TasksApi>();

        return services;
    }
}

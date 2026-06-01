using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Projects.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Projects;

public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProjectsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ProjectsDbContext.Schema)));

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectsApi, ProjectsApi>();

        return services;
    }
}

using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Auth.Services;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth;

/// <summary>
/// Composition root for the Auth module. <c>Program.cs</c> calls <see cref="AddAuthModule"/>;
/// controllers and MediatR handlers are discovered automatically by assembly scan.
/// </summary>
public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthService, AuthService>();

        // Public cross-module API (Rule 2).
        services.AddScoped<IAuthApi, AuthApi>();

        return services;
    }
}

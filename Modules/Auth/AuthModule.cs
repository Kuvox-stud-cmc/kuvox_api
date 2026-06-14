using System.Text;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kuvox.Api.Modules.Auth;

/// <summary>
/// Composition root for the Auth module. <c>Program.cs</c> calls <see cref="AddAuthModule"/>;
/// controllers and MediatR handlers are discovered automatically by assembly scan.
/// </summary>
public static class AuthModule
{
    /// <summary>Authorization policy: requires a studio <c>Admin</c> role claim.</summary>
    public const string RequireAdminPolicy = "RequireAdmin";

    /// <summary>Authorization policy: requires a <c>Creator</c> plan claim.</summary>
    public const string RequireCreatorPolicy = "RequireCreator";

    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStudioRepository, StudioRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStudioService, StudioService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        // Public cross-module API (Rule 2).
        services.AddScoped<IAuthApi, AuthApi>();

        // Email sending (SendGrid or dev log fallback) and frontend URL for email links.
        services.AddEmailInfrastructure(configuration);
        services.Configure<FrontendOptions>(configuration.GetSection(FrontendOptions.SectionName));

        // JWT options bound from the "Jwt" config section.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(RequireAdminPolicy, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.FindAll(TokenService.StudioClaimType)
                        .Any(c => c.Value.EndsWith($":{Enums.UserStudioRole.Admin}", StringComparison.Ordinal))))
            .AddPolicy(RequireCreatorPolicy, policy =>
                policy.RequireClaim(TokenService.PlanClaimType, Enums.UserPlan.Creator.ToString()));

        return services;
    }
}

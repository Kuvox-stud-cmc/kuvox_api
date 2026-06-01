using Kuvox.Api.Modules.Auth;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Projects;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Timelines;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Videos;
using Kuvox.Api.Modules.Videos.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Honest 501s for the scaffolded-but-unimplemented "real" endpoints.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<NotImplementedExceptionHandler>();

// Cross-module events (Rule 4): scan this assembly for INotificationHandler<>.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AuthModule).Assembly));

// Modules — each owns its own DbContext/schema (Rule 3) and registers its own services.
builder.Services
    .AddAuthModule(builder.Configuration)
    .AddProjectsModule(builder.Configuration)
    .AddVideosModule(builder.Configuration)
    .AddTimelinesModule(builder.Configuration);

const string FrontendCorsPolicy = "FrontendCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// Respect X-Forwarded-* headers set by the Nginx reverse proxy so the app
// sees the original scheme/host (https) instead of the internal http request.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Apply every module's migrations on startup so `docker compose up` + `dotnet run`
    // yields a fully schema'd database with no manual `dotnet ef` steps in dev.
    await app.MigrateModulesAsync();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.MapControllers();

app.Run();

internal static class StartupExtensions
{
    /// <summary>Migrates each module's DbContext (dev convenience — see Program.cs).</summary>
    public static async Task MigrateModulesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<VideosDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<TimelinesDbContext>().Database.MigrateAsync();
    }
}

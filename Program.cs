using DotNetEnv;
using Kuvox.Api.Modules.Auth;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Projects;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Timelines;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Media;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Notifications.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

// Load a local .env file if present (no-op in Docker where env vars are injected
// directly via env_file). Lets `.env` work for non-containerized dev too.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Structured logging (ABOUT.md goal). Levels/overrides are config-driven (appsettings
// "Serilog" section) so ops can tune without a redeploy; only the console *format* is
// environment-selected here — readable text in Development, compact JSON elsewhere —
// because mixing both via merged appsettings produces an ambiguous sink config.
builder.Services.AddSerilog((sp, lc) =>
{
    lc.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext();

    if (builder.Environment.IsDevelopment())
    {
        lc.WriteTo.Console();
    }
    else
    {
        lc.WriteTo.Console(new CompactJsonFormatter());
    }
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Exception handlers run in registration order; the first to claim an exception wins, so the
// specific ones (501 scaffolds, auth 401/4xx, domain 4xx) precede the catch-all 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<NotImplementedExceptionHandler>();
builder.Services.AddExceptionHandler<AuthExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Cross-module events (Rule 4): scan this assembly for INotificationHandler<>.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AuthModule).Assembly));

// Modules — each owns its own DbContext/schema (Rule 3) and registers its own services.
builder.Services
    .AddAuthModule(builder.Configuration)
    .AddProjectsModule(builder.Configuration)
    .AddMediaModule(builder.Configuration)
    .AddTimelinesModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration);

// Cross-module maintenance: hourly auto-purge of >7-day-old Trash (plan §2).
builder.Services.AddHostedService<TrashPurgeService>();

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

// One structured log line per request (method, path, status, elapsed).
app.UseSerilogRequestLogging();

app.UseExceptionHandler();

// OpenAPI + Scalar docs. Served in every environment (gated by config flag
// "Api:EnableDocs", default true) so the deployed instance exposes docs without
// having to run as Development.
if (app.Configuration.GetValue("Api:EnableDocs", true))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Apply every module's migrations on startup so `docker compose up` + `dotnet run`
// yields a fully schema'd database with no manual `dotnet ef` steps in any env.
await app.MigrateModulesAsync();

// Dev-only: seed a single pre-verified user so local work skips the SendGrid
// email-verification round-trip. No-op outside Development.
await app.SeedDevUserAsync();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

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
        await sp.GetRequiredService<MediaDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<TimelinesDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }
}

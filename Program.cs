using DotNetEnv;
using Kuvox.Api.Modules.Auth;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Projects;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.Metrics;
using Kuvox.Api.Modules.Shared.Infrastructure.Health;
using Kuvox.Api.Modules.Shared.Infrastructure.Http;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Kuvox.Api.Modules.Timelines;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Media;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Tasks;
using Kuvox.Api.Modules.Tasks.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
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
builder.Services.AddSignalR();
builder.Services.AddCachingInfrastructure(builder.Configuration);
builder.Services.AddSingleton<DatabaseCommandMetricsInterceptor>();
builder.Services.AddScoped<IPostgresReadinessProbe, PostgresReadinessProbe>();

// Exception handlers run in registration order; the first to claim an exception wins, so the
// specific ones (501 scaffolds, auth 401/4xx, domain 4xx) precede the catch-all 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<NotImplementedExceptionHandler>();
builder.Services.AddExceptionHandler<AuthExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Cross-module events (Rule 4): scan this assembly for INotificationHandler<>.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AuthModule).Assembly));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq")
);
builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection("Messaging")
);
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddSingleton<RabbitMqRetryHelper>();

// Modules — each owns its own DbContext/schema (Rule 3) and registers its own services.
builder.Services
    .AddAuthModule(builder.Configuration)
    .AddProjectsModule(builder.Configuration)
    .AddMediaModule(builder.Configuration)
    .AddTimelinesModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddTasksModule(builder.Configuration);

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

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers.TryGetValue("x-request-id", out var requestHeader)
        && !string.IsNullOrWhiteSpace(requestHeader.ToString())
            ? requestHeader.ToString()
            : context.TraceIdentifier;
    var editorCorrelationId = context.Request.Headers.TryGetValue("x-kuvox-editor-correlation-id", out var editorHeader)
        && !string.IsNullOrWhiteSpace(editorHeader.ToString())
            ? editorHeader.ToString()
            : requestId;

    context.TraceIdentifier = requestId;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["x-request-id"] = requestId;
        context.Response.Headers["x-kuvox-editor-correlation-id"] = editorCorrelationId;
        return Task.CompletedTask;
    });

    using (LogContext.PushProperty("RequestId", requestId))
    using (LogContext.PushProperty("EditorCorrelationId", editorCorrelationId))
    {
        await next();
    }
});

// One structured log line per request (method, path, status, elapsed).
app.UseSerilogRequestLogging();

app.UseMiddleware<DefaultCacheControlMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<HttpMetricsMiddleware>();

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

// Dev-only: seed a single pre-verified user so local work skips the email-verification
// round-trip. No-op outside Development.
await app.SeedDevUserAsync();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<MediaHub>("/hubs/media");
if (app.Services.GetRequiredService<IOptions<MetricsOptions>>().Value.Enabled)
{
    app.MapMetrics("/metrics");
}

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
        await sp.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
    }
}

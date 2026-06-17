using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth;

/// <summary>
/// Development-only convenience: seeds a single pre-verified user on startup so local work
/// doesn't need the SendGrid email-verification round-trip (which quickly burns the free-tier
/// quota). Idempotent and a hard no-op outside the Development environment, so it can never
/// create a backdoor account in staging/production.
/// </summary>
public static class AuthDevSeeder
{
    /// <summary>
    /// Seeds the mock user described by the <c>DevSeed</c> config section (with safe defaults)
    /// if it doesn't already exist. Call after migrations have been applied.
    /// </summary>
    public static async Task SeedDevUserAsync(this WebApplication app)
    {
        // Safety gate: only ever runs locally.
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var email = (app.Configuration["DevSeed:Email"] ?? "dev@kuvox.local").Trim().ToLowerInvariant();
        var password = app.Configuration["DevSeed:Password"] ?? "Password123!";
        var displayName = app.Configuration["DevSeed:DisplayName"] ?? "Dev User";

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AuthDbContext>();

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            app.Logger.LogInformation("Dev seed: user {Email} already exists — skipping.", email);
            return;
        }

        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = string.Empty,
            // Pre-verified: bypasses the hard login gate without sending an email.
            EmailVerifiedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        app.Logger.LogInformation(
            "Dev seed: created pre-verified user {Email} (password from DevSeed config).", email);
    }
}

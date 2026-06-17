using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth;

/// <summary>
/// Development-only convenience: seeds a set of pre-verified users plus a sample studio with
/// mixed-role memberships on startup, so local work doesn't need the SendGrid
/// email-verification round-trip (which quickly burns the free-tier quota) and the studio /
/// team features have realistic data to exercise. Fully idempotent and a hard no-op outside the
/// Development environment, so it can never create backdoor accounts in staging/production.
/// </summary>
public static class AuthDevSeeder
{
    /// <summary>Shared password for every seeded account (overridable via <c>DevSeed:Password</c>).</summary>
    private const string DefaultPassword = "Password123!";

    /// <summary>Name of the sample team used to test studio functionality.</summary>
    private const string StudioName = "Dev Studio";

    /// <summary>
    /// One seed account. <paramref name="StudioRole"/> is the membership role in <see cref="StudioName"/>,
    /// or <c>null</c> to leave the user with no team (useful for testing invites/adds).
    /// </summary>
    private sealed record SeedUser(string Email, string DisplayName, UserStudioRole? StudioRole);

    /// <summary>
    /// The primary account (configurable via the <c>DevSeed</c> section) is the studio Admin;
    /// the rest are fixed fixtures: a second admin, a regular member, and a teamless user.
    /// </summary>
    private static SeedUser[] BuildSeedUsers(IConfiguration config) =>
    [
        new((config["DevSeed:Email"] ?? "dev@kuvox.local").Trim().ToLowerInvariant(),
            config["DevSeed:DisplayName"] ?? "Dev User", UserStudioRole.Admin),
        new("alice@kuvox.local", "Alice Admin", UserStudioRole.Admin),
        new("bob@kuvox.local", "Bob Member", UserStudioRole.User),
        new("carol@kuvox.local", "Carol Solo", null),
    ];

    /// <summary>
    /// Seeds the accounts + sample studio if they don't already exist. Call after migrations
    /// have been applied. Safe to run on every startup.
    /// </summary>
    public static async Task SeedDevUserAsync(this WebApplication app)
    {
        // Safety gate: only ever runs locally.
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var password = app.Configuration["DevSeed:Password"] ?? DefaultPassword;
        var seedUsers = BuildSeedUsers(app.Configuration);

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AuthDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();

        // 1. Users — create any that are missing.
        var usersByEmail = new Dictionary<string, User>(StringComparer.Ordinal);
        foreach (var seed in seedUsers)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == seed.Email);
            if (existing is not null)
            {
                usersByEmail[seed.Email] = existing;
                continue;
            }

            var user = new User
            {
                Email = seed.Email,
                DisplayName = seed.DisplayName,
                PasswordHash = string.Empty,
                // Pre-verified: bypasses the hard login gate without sending an email.
                EmailVerifiedAt = DateTimeOffset.UtcNow,
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            usersByEmail[seed.Email] = user;
            app.Logger.LogInformation("Dev seed: created pre-verified user {Email}.", seed.Email);
        }
        await db.SaveChangesAsync();

        // 2. Studio — create the sample team if it's missing.
        var studio = await db.Studios.FirstOrDefaultAsync(s => s.Name == StudioName);
        if (studio is null)
        {
            studio = new Studio { Name = StudioName };
            db.Studios.Add(studio);
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Dev seed: created studio {Studio}.", StudioName);
        }

        // 3. Memberships — add any missing rows for users that should belong to the studio.
        foreach (var seed in seedUsers)
        {
            if (seed.StudioRole is not { } role)
            {
                continue;
            }

            var userId = usersByEmail[seed.Email].Id;
            var alreadyMember = await db.UserStudios
                .AnyAsync(us => us.UserId == userId && us.StudioId == studio.Id);
            if (alreadyMember)
            {
                continue;
            }

            db.UserStudios.Add(new UserStudio
            {
                UserId = userId,
                StudioId = studio.Id,
                Role = role,
            });
            app.Logger.LogInformation(
                "Dev seed: added {Email} to {Studio} as {Role}.", seed.Email, StudioName, role);
        }
        await db.SaveChangesAsync();
    }
}

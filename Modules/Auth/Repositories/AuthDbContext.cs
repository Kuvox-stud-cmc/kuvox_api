using Kuvox.Api.Modules.Auth.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>
/// EF Core context owning the Auth module's tables (Rule 3). Pinned to the <c>auth</c>
/// Postgres schema with its own migrations-history table so the module can migrate — and
/// later be extracted — independently.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public const string Schema = "auth";

    public DbSet<User> Users => Set<User>();

    public DbSet<Studio> Studios => Set<Studio>();

    public DbSet<UserStudio> UserStudios => Set<UserStudio>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(u => u.Plan).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Studio>(entity =>
        {
            entity.ToTable("studios");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(128).IsRequired(); 
        });

        modelBuilder.Entity<UserStudio>(entity =>
        {
           entity.ToTable("user_studios");
           entity.HasKey(us => new { us.UserId, us.StudioId });
           entity.Property(us => us.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

           entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Studio>()
                .WithMany()
                .HasForeignKey(us => us.StudioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(rt => rt.Id);
            entity.Property(rt => rt.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.Property(rt => rt.ReplacedByTokenHash).HasMaxLength(128);
            entity.Ignore(rt => rt.IsActive);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

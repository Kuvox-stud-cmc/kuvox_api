using Kuvox.Api.Modules.Auth.Enums;
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

    public DbSet<StudioInvitation> StudioInvitations => Set<StudioInvitation>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();

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
            entity.Property(u => u.EmailVerifiedAt);
            entity.Property(u => u.EmailNotificationsEnabled).IsRequired();
            entity.Property(u => u.ProductUpdatesEnabled).IsRequired();
            entity.Property(u => u.WeeklyDigestEnabled).IsRequired();
            entity.Property(u => u.DefaultEditorMode).HasMaxLength(16).IsRequired();
            entity.Property(u => u.Personality).HasConversion<string>().HasMaxLength(32).HasDefaultValue(UserPersonality.Casual).IsRequired();
            entity.Property(u => u.CreationGoalsJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
            entity.Property(u => u.OnboardingCompletedAt);
        });

        modelBuilder.Entity<Studio>(entity =>
        {
            entity.ToTable("studios");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(128).IsRequired(); 
            entity.Property(s => s.Description).HasMaxLength(1000);
            entity.Property(s => s.AvatarUrl).HasMaxLength(2048);
            entity.Property(s => s.PublicSlug).HasMaxLength(128);
            entity.HasIndex(s => s.PublicSlug).IsUnique().HasFilter("\"PublicSlug\" IS NOT NULL");
            entity.Property(s => s.InvitationExpiryDays).IsRequired();
            entity.Property(s => s.NotifyOnInvites).IsRequired();
            entity.Property(s => s.NotifyOnMembers).IsRequired();
            entity.Property(s => s.NotifyOnProjects).IsRequired();
            entity.Property(s => s.NotifyOnMedia).IsRequired();
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

        modelBuilder.Entity<StudioInvitation>(entity =>
        {
            entity.ToTable("studio_invitations");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Email).HasMaxLength(256).IsRequired();
            entity.Property(i => i.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.HasIndex(i => new { i.StudioId, i.Email, i.Status });
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

            entity.HasOne<Studio>()
                .WithMany()
                .HasForeignKey(i => i.StudioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(i => i.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("audit_log_entries");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.WorkspaceKind).HasMaxLength(32).IsRequired();
            entity.Property(a => a.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(128).IsRequired();
            entity.Property(a => a.TargetKind).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Summary).HasMaxLength(1000).IsRequired();
            entity.Property(a => a.MetadataJson).HasColumnType("jsonb");
            entity.HasIndex(a => new { a.WorkspaceId, a.CreatedAt });
            entity.HasIndex(a => a.Category);
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

        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.ToTable("auth_tokens");
            entity.HasKey(at => at.Id);
            entity.Property(at => at.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(at => at.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(at => at.TokenHash).IsUnique();
            entity.HasIndex(at => new { at.UserId, at.Purpose });
            entity.Ignore(at => at.IsActive);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(at => at.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

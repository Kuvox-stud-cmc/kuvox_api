using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Notifications.Repositories;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.UserId).IsRequired();
            entity.Property(n => n.StudioId).IsRequired();
            entity.Property(n => n.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(n => n.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(n => n.Message).HasMaxLength(2000).IsRequired();
        
            // Index for userId and studioId to efficiently query notifications for a user or a studio.
            entity.HasIndex(n => n.UserId);
            entity.HasIndex(n => n.StudioId);
        });

        base.OnModelCreating(modelBuilder);
    }
} 
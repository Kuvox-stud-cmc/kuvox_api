using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Projects.Repositories;

/// <summary>EF Core context owning the Projects module's tables in the <c>projects</c> schema (Rule 3).</summary>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    public const string Schema = "projects";

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectActivity> ProjectActivities => Set<ProjectActivity>();

    public DbSet<ProjectUser> ProjectUsers => Set<ProjectUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(p => p.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            // OwnerId references auth.users/auth.studios by id only — no cross-schema FK (Rule 1/3).
            entity.HasIndex(p => p.OwnerId);
            entity.HasIndex(p => new { p.OwnerKind, p.OwnerId });
            entity.HasIndex(p => p.DeletedAt);
        });

        modelBuilder.Entity<ProjectActivity>(entity =>
        {
            entity.ToTable("project_activities");
            entity.HasKey(pa => pa.Id);
            entity.Property(pa => pa.ProjectId).IsRequired();
            entity.Property(pa => pa.UserId).IsRequired();
            entity.Property(pa => pa.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(pa => pa.CreatedAt).IsRequired();
            entity.HasIndex(pa => pa.ProjectId);
            entity.HasIndex(pa => pa.UserId);
        });

        modelBuilder.Entity<ProjectUser>(entity =>
        {
            entity.ToTable("project_users");
            entity.HasKey(pu => new { pu.ProjectId, pu.UserId });
            entity.Property(pu => pu.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(pu => pu.IsStarred).HasDefaultValue(false).IsRequired();
            entity.Property(pu => pu.IsTemplate).HasDefaultValue(false).IsRequired();
            entity.HasIndex(pu => pu.UserId);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(pu => pu.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

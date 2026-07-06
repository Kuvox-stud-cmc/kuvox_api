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

    public DbSet<ProjectMedia> ProjectMedias => Set<ProjectMedia>();

    public DbSet<ProjectImage> ProjectImages => Set<ProjectImage>();

    public DbSet<ProjectAudio> ProjectAudios => Set<ProjectAudio>();

    public DbSet<ProjectVideo> ProjectVideos => Set<ProjectVideo>();

    public DbSet<ImageComposition> ImageCompositions => Set<ImageComposition>();

    public DbSet<ImageCompositionRevision> ImageCompositionRevisions => Set<ImageCompositionRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProjectMedia>().UseTpcMappingStrategy().HasKey(pm => new { pm.ProjectId, pm.MediaId });

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
            entity.Property(pu => pu.IsHidden).HasDefaultValue(false).IsRequired();
            entity.HasIndex(pu => pu.UserId);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(pu => pu.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectImage>(entity =>
        {
            entity.ToTable("project_images");
            entity.HasIndex(pm => pm.MediaId);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(pm => pm.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectAudio>(entity =>
        {
            entity.ToTable("project_audios");
            entity.HasIndex(pm => pm.MediaId);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(pm => pm.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectVideo>(entity =>
        {
            entity.ToTable("project_videos");
            entity.HasIndex(pm => pm.MediaId);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(pm => pm.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageComposition>(entity =>
        {
            entity.ToTable("image_compositions");
            entity.HasKey(composition => composition.Id);
            entity.Property(composition => composition.ProjectId).IsRequired();
            entity.Property(composition => composition.DocumentJson).HasColumnType("jsonb").IsRequired();
            entity.Property(composition => composition.RevisionNumber).IsRequired();
            entity.Property(composition => composition.UpdatedByUserId).IsRequired();
            entity.HasIndex(composition => composition.ProjectId).IsUnique();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(composition => composition.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageCompositionRevision>(entity =>
        {
            entity.ToTable("image_composition_revisions");
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.ImageCompositionId).IsRequired();
            entity.Property(revision => revision.ProjectId).IsRequired();
            entity.Property(revision => revision.RevisionNumber).IsRequired();
            entity.Property(revision => revision.DocumentJson).HasColumnType("jsonb").IsRequired();
            entity.Property(revision => revision.OperationsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(revision => revision.CreatedByUserId).IsRequired();
            entity.HasIndex(revision => revision.ProjectId);
            entity.HasIndex(revision => new { revision.ProjectId, revision.RevisionNumber }).IsUnique();

            entity.HasOne<ImageComposition>()
                .WithMany()
                .HasForeignKey(revision => revision.ImageCompositionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(revision => revision.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

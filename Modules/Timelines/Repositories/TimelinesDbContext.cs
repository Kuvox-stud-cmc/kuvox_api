using Kuvox.Api.Modules.Timelines.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Timelines.Repositories;

/// <summary>
/// EF Core context owning the Timelines module's four tables in the <c>timelines</c> schema
/// (Rule 3): timelines, timeline_revisions, render_jobs, command_history.
/// </summary>
public sealed class TimelinesDbContext(DbContextOptions<TimelinesDbContext> options) : DbContext(options)
{
    public const string Schema = "timelines";

    public DbSet<Timeline> Timelines => Set<Timeline>();
    public DbSet<TimelineRevision> TimelineRevisions => Set<TimelineRevision>();
    public DbSet<RenderJob> RenderJobs => Set<RenderJob>();
    public DbSet<CommandHistory> CommandHistory => Set<CommandHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Timeline>(entity =>
        {
            entity.ToTable("timelines");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.ProjectId);
        });

        modelBuilder.Entity<TimelineRevision>(entity =>
        {
            entity.ToTable("timeline_revisions");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Operations).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(r => new { r.TimelineId, r.RevisionNumber }).IsUnique();
        });

        modelBuilder.Entity<RenderJob>(entity =>
        {
            entity.ToTable("render_jobs");
            entity.HasKey(j => j.Id);
            entity.Property(j => j.Status).HasMaxLength(32).IsRequired();
            entity.Property(j => j.OutputStorageKey).HasMaxLength(1024);
            entity.HasIndex(j => j.TimelineId);
        });

        modelBuilder.Entity<CommandHistory>(entity =>
        {
            entity.ToTable("command_history");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.CommandText).HasMaxLength(2000).IsRequired();
            entity.Property(c => c.Intent).HasMaxLength(32);
            entity.HasIndex(c => c.ProjectId);
        });

        base.OnModelCreating(modelBuilder);
    }
}

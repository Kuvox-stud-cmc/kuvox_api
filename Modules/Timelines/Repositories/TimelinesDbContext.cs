using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Timeline>(entity =>
        {
            entity.ToTable("timelines");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.ProjectId).IsUnique();
        });

        modelBuilder.Entity<TimelineRevision>(entity =>
        {
            entity.ToTable("timeline_revisions");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Operations).HasColumnType("jsonb").IsRequired();
            entity.Property(r => r.DocumentJson).HasColumnType("jsonb").IsRequired();
            entity.Property(r => r.DocumentSchemaVersion).IsRequired();
            entity.Property(r => r.OperationsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(r => r.Source).HasMaxLength(64);
            entity.Property(r => r.Label).HasMaxLength(200);
            entity.Property(r => r.CreatedByUserId).IsRequired();
            entity.HasIndex(r => new { r.TimelineId, r.RevisionNumber }).IsUnique();
        });

        modelBuilder.Entity<RenderJob>(entity =>
        {
            entity.ToTable("render_jobs");
            entity.HasKey(j => j.Id);
            entity.Property(j => j.RequestedByUserId).IsRequired();
            entity.Property(j => j.SettingsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(j => j.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(j => j.OutputStorageKey).HasMaxLength(1024);
            entity.Property(j => j.OutputBucketName).HasMaxLength(256);
            entity.Property(j => j.OutputContentType).HasMaxLength(128);
            entity.Property(j => j.ErrorCode).HasMaxLength(128);
            entity.Property(j => j.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(j => j.TimelineId);
            entity.HasIndex(j => j.RevisionId);
        });

        modelBuilder.Entity<CommandHistory>(entity =>
        {
            entity.ToTable("command_history");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.CommandText).HasMaxLength(2000).IsRequired();
            entity.Property(c => c.Intent).HasMaxLength(32);
            entity.HasIndex(c => c.ProjectId);
        });

        ConfigureOutbox(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", "shared", table => table.ExcludeFromMigrations());
            entity.HasKey(message => message.Id);
            entity.Property(message => message.DedupeKey).HasMaxLength(256).IsRequired();
            entity.Property(message => message.Transport).HasMaxLength(32).IsRequired();
            entity.Property(message => message.Exchange).HasMaxLength(256).IsRequired();
            entity.Property(message => message.RoutingKey).HasMaxLength(256).IsRequired();
            entity.Property(message => message.EventType).HasMaxLength(256).IsRequired();
            entity.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(message => message.HeadersJson).HasColumnType("jsonb").IsRequired();
            entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(message => message.LastError).HasMaxLength(2048);
            entity.HasIndex(message => message.DedupeKey).IsUnique();
            entity.HasIndex(message => new { message.Status, message.NextAttemptAt });
        });
    }
}

using Kuvox.Api.Modules.Videos.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Videos.Repositories;

/// <summary>EF Core context owning the Videos module's tables in the <c>videos</c> schema (Rule 3).</summary>
public sealed class VideosDbContext(DbContextOptions<VideosDbContext> options) : DbContext(options)
{
    public const string Schema = "videos";

    public DbSet<Video> Videos => Set<Video>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Video>(entity =>
        {
            entity.ToTable("videos");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Filename).HasMaxLength(512).IsRequired();
            entity.Property(v => v.StorageKey).HasMaxLength(1024).IsRequired();
            entity.Property(v => v.Codec).HasMaxLength(64);
            entity.Property(v => v.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(v => v.ProjectId);
        });

        base.OnModelCreating(modelBuilder);
    }
}

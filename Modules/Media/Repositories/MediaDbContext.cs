using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Media.Repositories;

/// <summary>EF Core context owning the Media module's tables in the <c>media</c> schema (Rule 3).</summary>
public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options)
{
    public const string Schema = "media";

    // The entity type is qualified as Models.Media throughout the module because the bare
    // name "Media" collides with the Kuvox.Api.Modules.Media namespace (CS0118).
    public DbSet<Models.Media> Media => Set<Models.Media>();

    public DbSet<Models.MediaUser> MediaUsers => Set<Models.MediaUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Models.Media>(entity =>
        {
            entity.ToTable("media");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(m => m.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(m => m.Filename).HasMaxLength(512).IsRequired();
            entity.Property(m => m.StorageKey).HasMaxLength(1024).IsRequired();
            entity.Property(m => m.Codec).HasMaxLength(64);
            entity.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(m => m.ProjectId);
            entity.HasIndex(m => new { m.OwnerKind, m.OwnerId });
            entity.HasIndex(m => m.DeletedAt);
        });

        modelBuilder.Entity<Models.MediaUser>(entity =>
        {
            entity.ToTable("media_users");
            entity.HasKey(mu => new { mu.MediaId, mu.UserId });
            entity.Property(mu => mu.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(mu => mu.IsFavorite).HasDefaultValue(false).IsRequired();
            entity.Property(mu => mu.AlbumId).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(mu => mu.UserId);

            entity.HasOne<Models.Media>()
                .WithMany()
                .HasForeignKey(mu => mu.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Album>(entity =>
        {
            entity.ToTable("albums");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).HasMaxLength(256).IsRequired();
            entity.Property(a => a.Description).HasMaxLength(1024).IsRequired();
        });

        modelBuilder.Entity<Models.AlbumUser>(entity =>
        {
            entity.ToTable("album_users");
            entity.HasKey(au => new { au.AlbumId, au.UserId });
            entity.Property(au => au.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        
            entity.HasOne<Models.Album>()
                .WithMany()
                .HasForeignKey(au => au.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

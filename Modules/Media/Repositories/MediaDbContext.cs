using Microsoft.EntityFrameworkCore;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

namespace Kuvox.Api.Modules.Media.Repositories;

/// <summary>EF Core context owning the Media module's tables in the <c>media</c> schema (Rule 3).</summary>
public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options)
{
    public const string Schema = "media";

    public DbSet<Models.Media> Media => Set<Models.Media>();
    public DbSet<Models.MediaUser> MediaUsers => Set<Models.MediaUser>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Models.Video> Videos => Set<Models.Video>();
    public DbSet<Models.Audio> Audios => Set<Models.Audio>();
    public DbSet<Models.Photo> Photos => Set<Models.Photo>();
    public DbSet<Models.VideoUser> VideoUsers => Set<Models.VideoUser>();
    public DbSet<Models.AudioUser> AudioUsers => Set<Models.AudioUser>();
    public DbSet<Models.PhotoUser> PhotoUsers => Set<Models.PhotoUser>();

    public DbSet<Models.Album> Albums => Set<Models.Album>();
    public DbSet<Models.AlbumUser> AlbumUsers => Set<Models.AlbumUser>();

    public DbSet<Models.AlbumMedia> AlbumMedia => Set<Models.AlbumMedia>();
    public DbSet<Models.AlbumPhoto> AlbumPhotos => Set<Models.AlbumPhoto>();
    public DbSet<Models.AlbumAudio> AlbumAudios => Set<Models.AlbumAudio>();
    public DbSet<Models.AlbumVideo> AlbumVideos => Set<Models.AlbumVideo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Models.Media>().UseTpcMappingStrategy().HasKey(m => m.Id);
        modelBuilder.Entity<Models.MediaUser>().UseTpcMappingStrategy().HasKey(mu => new { mu.MediaId, mu.UserId });
        modelBuilder.Entity<Models.AlbumMedia>().UseTpcMappingStrategy().HasKey(am => new { am.AlbumId, am.MediaId });
        ConfigureOutbox(modelBuilder);

        modelBuilder.Entity<Models.Video>(entity =>
        {
            entity.ToTable("videos");
            entity.Property(m => m.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(m => m.Filename).HasMaxLength(512).IsRequired();
            entity.Property(m => m.StorageKey).HasMaxLength(1024).IsRequired();
            ConfigureOptimizedStorage(entity);
            entity.Property(m => m.Codec).HasMaxLength(64);
            entity.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(m => m.ErrorMessage).HasMaxLength(1024);
            entity.Property(m => m.ArchiveStorageKey).HasMaxLength(1024);
            entity.Property(m => m.ArchiveReason).HasMaxLength(1024);
            entity.Property(m => m.DurationSeconds).HasPrecision(18, 6).IsRequired();
            entity.Property(m => m.Width).IsRequired();
            entity.Property(m => m.Height).IsRequired();
            entity.Property(m => m.FrameRate).HasPrecision(18, 6).IsRequired();
            entity.Property(m => m.SizeBytes).IsRequired();
            entity.HasIndex(m => new { m.OwnerKind, m.OwnerId });
            entity.HasIndex(m => m.DeletedAt);
        });

        modelBuilder.Entity<Models.Audio>(entity =>
        {
            entity.ToTable("audios");
            entity.Property(a => a.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(a => a.Filename).HasMaxLength(512).IsRequired();
            entity.Property(a => a.StorageKey).HasMaxLength(1024).IsRequired();
            ConfigureOptimizedStorage(entity);
            entity.Property(a => a.Codec).HasMaxLength(64);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(a => a.ErrorMessage).HasMaxLength(1024);
            entity.Property(a => a.ArchiveStorageKey).HasMaxLength(1024);
            entity.Property(a => a.ArchiveReason).HasMaxLength(1024);
            entity.Property(a => a.DurationSeconds).HasPrecision(18, 6).IsRequired();
            entity.Property(a => a.SizeBytes).IsRequired();
            entity.HasIndex(a => new { a.OwnerKind, a.OwnerId });
            entity.HasIndex(a => a.DeletedAt);
        });

        modelBuilder.Entity<Models.Photo>(entity =>
        {
            entity.ToTable("photos");
            entity.Property(p => p.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(p => p.Filename).HasMaxLength(512).IsRequired();
            entity.Property(p => p.StorageKey).HasMaxLength(1024).IsRequired();
            ConfigureOptimizedStorage(entity);
            entity.Property(p => p.Codec).HasMaxLength(64);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(p => p.ErrorMessage).HasMaxLength(1024);
            entity.Property(p => p.ArchiveStorageKey).HasMaxLength(1024);
            entity.Property(p => p.ArchiveReason).HasMaxLength(1024);
            entity.Property(p => p.Width).IsRequired();
            entity.Property(p => p.Height).IsRequired();
            entity.HasIndex(p => new { p.OwnerKind, p.OwnerId });
            entity.HasIndex(p => p.DeletedAt);
        });

        modelBuilder.Entity<Models.VideoUser>(entity =>
        {
            entity.ToTable("video_users");
            entity.Property(mu => mu.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(mu => mu.IsFavorite).HasDefaultValue(false).IsRequired();
            entity.HasIndex(mu => mu.UserId);

            entity.HasOne<Models.Video>()
                .WithMany()
                .HasForeignKey(mu => mu.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.AudioUser>(entity =>
        {
            entity.ToTable("audio_users");
            entity.Property(mu => mu.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(mu => mu.IsFavorite).HasDefaultValue(false).IsRequired();
            entity.HasIndex(mu => mu.UserId);

            entity.HasOne<Models.Audio>()
                .WithMany()
                .HasForeignKey(mu => mu.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.PhotoUser>(entity =>
        {
            entity.ToTable("photo_users");
            entity.Property(mu => mu.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(mu => mu.IsFavorite).HasDefaultValue(false).IsRequired();
            entity.HasIndex(mu => mu.UserId);

            entity.HasOne<Models.Photo>()
                .WithMany()
                .HasForeignKey(mu => mu.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Album>(entity =>
        {
            entity.ToTable("albums");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.OwnerKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(a => a.Name).HasMaxLength(256).IsRequired();
            entity.Property(a => a.Description).HasMaxLength(1024).IsRequired();
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(a => a.MaterialSymbol).HasMaxLength(64).IsRequired();
            entity.Property(a => a.IsDeleteAble).IsRequired();
            entity.HasIndex(a => new { a.OwnerKind, a.OwnerId });
        });

        modelBuilder.Entity<Models.AlbumUser>(entity =>
        {
            entity.ToTable("album_users");
            entity.HasKey(au => new { au.AlbumId, au.UserId });
            entity.Property(au => au.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(au => au.IsFavorite).HasDefaultValue(false).IsRequired();
        
            entity.HasOne<Models.Album>()
                .WithMany()
                .HasForeignKey(au => au.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.AlbumAudio>(entity =>
        {
            entity.ToTable("album_audios");

            entity.HasOne<Models.Album>()
                .WithMany()
                .HasForeignKey(am => am.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Models.Audio>()
                .WithMany()
                .HasForeignKey(am => am.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.AlbumVideo>(entity =>
        {
            entity.ToTable("album_videos");

            entity.HasOne<Models.Album>()
                .WithMany()
                .HasForeignKey(am => am.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Models.Video>()
                .WithMany()
                .HasForeignKey(am => am.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.AlbumPhoto>(entity =>
        {
            entity.ToTable("album_photos");

            entity.HasOne<Models.Album>()
                .WithMany()
                .HasForeignKey(am => am.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Models.Photo>()
                .WithMany()
                .HasForeignKey(am => am.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureOptimizedStorage<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : Models.Media
    {
        entity.Property(m => m.RawBucketName).HasMaxLength(256);
        entity.Property(m => m.RawStorageKey).HasMaxLength(1024);
        entity.Property(m => m.CanonicalBucketName).HasMaxLength(256);
        entity.Property(m => m.CanonicalStorageKey).HasMaxLength(1024);
        entity.Property(m => m.ProxyBucketName).HasMaxLength(256);
        entity.Property(m => m.ProxyStorageKey).HasMaxLength(1024);
        entity.Property(m => m.ThumbnailBucketName).HasMaxLength(256);
        entity.Property(m => m.ThumbnailStorageKey).HasMaxLength(1024);
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", "shared");
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

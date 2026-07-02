using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMediaOptimizationPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_videos_ProjectId",
                schema: "media",
                table: "videos");

            migrationBuilder.DropIndex(
                name: "IX_photos_ProjectId",
                schema: "media",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "IX_audios_ProjectId",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "media",
                table: "audios");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalBucketName",
                schema: "media",
                table: "videos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "videos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "videos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyBucketName",
                schema: "media",
                table: "videos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProxySizeBytes",
                schema: "media",
                table: "videos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyStorageKey",
                schema: "media",
                table: "videos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawBucketName",
                schema: "media",
                table: "videos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RawSizeBytes",
                schema: "media",
                table: "videos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawStorageKey",
                schema: "media",
                table: "videos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "videos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "videos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "videos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalBucketName",
                schema: "media",
                table: "photos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "photos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyBucketName",
                schema: "media",
                table: "photos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProxySizeBytes",
                schema: "media",
                table: "photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyStorageKey",
                schema: "media",
                table: "photos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawBucketName",
                schema: "media",
                table: "photos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RawSizeBytes",
                schema: "media",
                table: "photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawStorageKey",
                schema: "media",
                table: "photos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "photos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "photos",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalBucketName",
                schema: "media",
                table: "audios",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "audios",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "audios",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyBucketName",
                schema: "media",
                table: "audios",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProxySizeBytes",
                schema: "media",
                table: "audios",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyStorageKey",
                schema: "media",
                table: "audios",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawBucketName",
                schema: "media",
                table: "audios",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RawSizeBytes",
                schema: "media",
                table: "audios",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawStorageKey",
                schema: "media",
                table: "audios",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "audios",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "audios",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "audios",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanonicalBucketName",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ProxyBucketName",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ProxySizeBytes",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ProxyStorageKey",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "RawBucketName",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "RawSizeBytes",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "RawStorageKey",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "CanonicalBucketName",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ProxyBucketName",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ProxySizeBytes",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ProxyStorageKey",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "RawBucketName",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "RawSizeBytes",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "RawStorageKey",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "CanonicalBucketName",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "CanonicalSizeBytes",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "CanonicalStorageKey",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ProxyBucketName",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ProxySizeBytes",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ProxyStorageKey",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "RawBucketName",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "RawSizeBytes",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "RawStorageKey",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ThumbnailBucketName",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ThumbnailSizeBytes",
                schema: "media",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "ThumbnailStorageKey",
                schema: "media",
                table: "audios");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "media",
                table: "videos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "media",
                table: "photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "media",
                table: "audios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_videos_ProjectId",
                schema: "media",
                table: "videos",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_photos_ProjectId",
                schema: "media",
                table: "photos",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_audios_ProjectId",
                schema: "media",
                table: "audios",
                column: "ProjectId");
        }
    }
}

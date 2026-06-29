using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddAlbumMediaRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlbumId",
                schema: "media",
                table: "video_users");

            migrationBuilder.DropColumn(
                name: "AlbumId",
                schema: "media",
                table: "photo_users");

            migrationBuilder.DropColumn(
                name: "AlbumId",
                schema: "media",
                table: "audio_users");

            migrationBuilder.CreateTable(
                name: "album_audios",
                schema: "media",
                columns: table => new
                {
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_album_audios", x => new { x.AlbumId, x.MediaId });
                    table.ForeignKey(
                        name: "FK_album_audios_albums_AlbumId",
                        column: x => x.AlbumId,
                        principalSchema: "media",
                        principalTable: "albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_album_audios_audios_MediaId",
                        column: x => x.MediaId,
                        principalSchema: "media",
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_photos",
                schema: "media",
                columns: table => new
                {
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_album_photos", x => new { x.AlbumId, x.MediaId });
                    table.ForeignKey(
                        name: "FK_album_photos_albums_AlbumId",
                        column: x => x.AlbumId,
                        principalSchema: "media",
                        principalTable: "albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_album_photos_photos_MediaId",
                        column: x => x.MediaId,
                        principalSchema: "media",
                        principalTable: "photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_videos",
                schema: "media",
                columns: table => new
                {
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_album_videos", x => new { x.AlbumId, x.MediaId });
                    table.ForeignKey(
                        name: "FK_album_videos_albums_AlbumId",
                        column: x => x.AlbumId,
                        principalSchema: "media",
                        principalTable: "albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_album_videos_videos_MediaId",
                        column: x => x.MediaId,
                        principalSchema: "media",
                        principalTable: "videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_album_audios_MediaId",
                schema: "media",
                table: "album_audios",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_album_photos_MediaId",
                schema: "media",
                table: "album_photos",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_album_videos_MediaId",
                schema: "media",
                table: "album_videos",
                column: "MediaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "album_audios",
                schema: "media");

            migrationBuilder.DropTable(
                name: "album_photos",
                schema: "media");

            migrationBuilder.DropTable(
                name: "album_videos",
                schema: "media");

            migrationBuilder.AddColumn<string>(
                name: "AlbumId",
                schema: "media",
                table: "video_users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AlbumId",
                schema: "media",
                table: "photo_users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AlbumId",
                schema: "media",
                table: "audio_users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }
    }
}

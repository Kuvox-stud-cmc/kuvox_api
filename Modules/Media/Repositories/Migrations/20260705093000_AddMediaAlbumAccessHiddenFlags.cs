using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    public partial class AddMediaAlbumAccessHiddenFlags : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "media",
                table: "video_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "media",
                table: "photo_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "media",
                table: "audio_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "media",
                table: "album_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "media",
                table: "video_users");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "media",
                table: "photo_users");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "media",
                table: "audio_users");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "media",
                table: "album_users");
        }
    }
}

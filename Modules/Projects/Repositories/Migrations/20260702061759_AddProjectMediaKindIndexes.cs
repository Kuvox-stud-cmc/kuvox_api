using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Projects.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMediaKindIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_project_videos_MediaId",
                schema: "projects",
                table: "project_videos",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_project_images_MediaId",
                schema: "projects",
                table: "project_images",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_project_audios_MediaId",
                schema: "projects",
                table: "project_audios",
                column: "MediaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_videos_MediaId",
                schema: "projects",
                table: "project_videos");

            migrationBuilder.DropIndex(
                name: "IX_project_images_MediaId",
                schema: "projects",
                table: "project_images");

            migrationBuilder.DropIndex(
                name: "IX_project_audios_MediaId",
                schema: "projects",
                table: "project_audios");
        }
    }
}

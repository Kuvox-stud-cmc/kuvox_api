using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Projects.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectUserFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStarred",
                schema: "projects",
                table: "project_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                schema: "projects",
                table: "project_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStarred",
                schema: "projects",
                table: "project_users");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                schema: "projects",
                table: "project_users");
        }
    }
}

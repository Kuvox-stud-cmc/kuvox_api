using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Auth.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultEditorMode",
                schema: "auth",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProductUpdatesEnabled",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklyDigestEnabled",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultEditorMode",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProductUpdatesEnabled",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "WeeklyDigestEnabled",
                schema: "auth",
                table: "users");
        }
    }
}

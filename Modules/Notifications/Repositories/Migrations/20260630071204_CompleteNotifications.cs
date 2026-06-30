using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Notifications.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class CompleteNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "StudioId",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "LinkUrl",
                schema: "notifications",
                table: "notifications",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                schema: "notifications",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_Status_CreatedAt",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "UserId", "Status", "CreatedAt" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId_Status_CreatedAt",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "LinkUrl",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "StudioId",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

        }
    }
}

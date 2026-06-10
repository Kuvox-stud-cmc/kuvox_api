using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Projects.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectKindsOwnersTrashAndProjectUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "projects",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "projects",
                table: "projects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerKind",
                schema: "projects",
                table: "projects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "project_users",
                schema: "projects",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_users", x => new { x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_project_users_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projects_DeletedAt",
                schema: "projects",
                table: "projects",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerKind_OwnerId",
                schema: "projects",
                table: "projects",
                columns: new[] { "OwnerKind", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_users_UserId",
                schema: "projects",
                table: "project_users",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_users",
                schema: "projects");

            migrationBuilder.DropIndex(
                name: "IX_projects_DeletedAt",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_projects_OwnerKind_OwnerId",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                schema: "projects",
                table: "projects");
        }
    }
}

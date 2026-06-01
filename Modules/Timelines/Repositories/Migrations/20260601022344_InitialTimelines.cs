using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Timelines.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class InitialTimelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "timelines");

            migrationBuilder.CreateTable(
                name: "command_history",
                schema: "timelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Intent = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "render_jobs",
                schema: "timelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OutputStorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_render_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timeline_revisions",
                schema: "timelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Operations = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timeline_revisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timelines",
                schema: "timelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timelines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_command_history_ProjectId",
                schema: "timelines",
                table: "command_history",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_render_jobs_TimelineId",
                schema: "timelines",
                table: "render_jobs",
                column: "TimelineId");

            migrationBuilder.CreateIndex(
                name: "IX_timeline_revisions_TimelineId_RevisionNumber",
                schema: "timelines",
                table: "timeline_revisions",
                columns: new[] { "TimelineId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_timelines_ProjectId",
                schema: "timelines",
                table: "timelines",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_history",
                schema: "timelines");

            migrationBuilder.DropTable(
                name: "render_jobs",
                schema: "timelines");

            migrationBuilder.DropTable(
                name: "timeline_revisions",
                schema: "timelines");

            migrationBuilder.DropTable(
                name: "timelines",
                schema: "timelines");
        }
    }
}

using System;
using Kuvox.Api.Modules.Timelines.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Timelines.Repositories.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TimelinesDbContext))]
    [Migration("20260706140000_AddTimelineDocumentSnapshots")]
    public partial class AddTimelineDocumentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_timelines_ProjectId",
                schema: "timelines",
                table: "timelines");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "timelines",
                table: "timeline_revisions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "DocumentJson",
                schema: "timelines",
                table: "timeline_revisions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "DocumentSchemaVersion",
                schema: "timelines",
                table: "timeline_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                schema: "timelines",
                table: "timeline_revisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationsJson",
                schema: "timelines",
                table: "timeline_revisions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "timelines",
                table: "timeline_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_timelines_ProjectId",
                schema: "timelines",
                table: "timelines",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_timelines_ProjectId",
                schema: "timelines",
                table: "timelines");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.DropColumn(
                name: "DocumentJson",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.DropColumn(
                name: "DocumentSchemaVersion",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.DropColumn(
                name: "Label",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.DropColumn(
                name: "OperationsJson",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "timelines",
                table: "timeline_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_timelines_ProjectId",
                schema: "timelines",
                table: "timelines",
                column: "ProjectId");
        }
    }
}

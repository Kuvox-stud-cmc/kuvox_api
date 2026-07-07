using System;
using Kuvox.Api.Modules.Timelines.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Timelines.Repositories.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TimelinesDbContext))]
    [Migration("20260706153000_AddRenderJobRequestMetadata")]
    public partial class AddRenderJobRequestMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                schema: "timelines",
                table: "render_jobs",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                schema: "timelines",
                table: "render_jobs",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateIndex(
                name: "IX_render_jobs_RevisionId",
                schema: "timelines",
                table: "render_jobs",
                column: "RevisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_render_jobs_RevisionId",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                schema: "timelines",
                table: "render_jobs");
        }
    }
}

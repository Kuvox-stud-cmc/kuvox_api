using System;
using Kuvox.Api.Modules.Timelines.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Timelines.Repositories.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TimelinesDbContext))]
    [Migration("20260709090000_AddRenderJobResultMetadata")]
    public partial class AddRenderJobResultMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputBucketName",
                schema: "timelines",
                table: "render_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputContentType",
                schema: "timelines",
                table: "render_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OutputSizeBytes",
                schema: "timelines",
                table: "render_jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                schema: "timelines",
                table: "render_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                schema: "timelines",
                table: "render_jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                schema: "timelines",
                table: "render_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinishedAt",
                schema: "timelines",
                table: "render_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputBucketName",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "OutputContentType",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "OutputSizeBytes",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "timelines",
                table: "render_jobs");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                schema: "timelines",
                table: "render_jobs");
        }
    }
}

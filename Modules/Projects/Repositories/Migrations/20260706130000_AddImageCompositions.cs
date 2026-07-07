using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Projects.Repositories.Migrations
{
    [DbContext(typeof(ProjectsDbContext))]
    [Migration("20260706130000_AddImageCompositions")]
    public partial class AddImageCompositions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "image_compositions",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentJson = table.Column<string>(type: "jsonb", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_compositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_image_compositions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "image_composition_revisions",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageCompositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    DocumentJson = table.Column<string>(type: "jsonb", nullable: false),
                    OperationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_composition_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_image_composition_revisions_image_compositions_ImageComposit~",
                        column: x => x.ImageCompositionId,
                        principalSchema: "projects",
                        principalTable: "image_compositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_composition_revisions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_image_composition_revisions_ImageCompositionId",
                schema: "projects",
                table: "image_composition_revisions",
                column: "ImageCompositionId");

            migrationBuilder.CreateIndex(
                name: "IX_image_composition_revisions_ProjectId",
                schema: "projects",
                table: "image_composition_revisions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_image_composition_revisions_ProjectId_RevisionNumber",
                schema: "projects",
                table: "image_composition_revisions",
                columns: new[] { "ProjectId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_image_compositions_ProjectId",
                schema: "projects",
                table: "image_compositions",
                column: "ProjectId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_composition_revisions",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "image_compositions",
                schema: "projects");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Tasks.Repositories.Migrations
{
    [DbContext(typeof(TasksDbContext))]
    [Migration("20260705100000_InitialTasks")]
    public partial class InitialTasks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "tasks");

            migrationBuilder.CreateTable(
                name: "task_labels",
                schema: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_labels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_milestones",
                schema: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_milestones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_issues",
                schema: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MilestoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_issues_task_milestones_MilestoneId",
                        column: x => x.MilestoneId,
                        principalSchema: "tasks",
                        principalTable: "task_milestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_assignees",
                schema: "tasks",
                columns: table => new
                {
                    TaskIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_assignees", x => new { x.TaskIssueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_assignees_task_issues_TaskIssueId",
                        column: x => x.TaskIssueId,
                        principalSchema: "tasks",
                        principalTable: "task_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_issue_labels",
                schema: "tasks",
                columns: table => new
                {
                    TaskIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_issue_labels", x => new { x.TaskIssueId, x.TaskLabelId });
                    table.ForeignKey(
                        name: "FK_task_issue_labels_task_issues_TaskIssueId",
                        column: x => x.TaskIssueId,
                        principalSchema: "tasks",
                        principalTable: "task_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_issue_labels_task_labels_TaskLabelId",
                        column: x => x.TaskLabelId,
                        principalSchema: "tasks",
                        principalTable: "task_labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_assignees_UserId",
                schema: "tasks",
                table: "task_assignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_issue_labels_TaskLabelId",
                schema: "tasks",
                table: "task_issue_labels",
                column: "TaskLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_task_issues_MilestoneId",
                schema: "tasks",
                table: "task_issues",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_task_issues_ProjectId",
                schema: "tasks",
                table: "task_issues",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_task_issues_StudioId",
                schema: "tasks",
                table: "task_issues",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_task_issues_StudioId_Status",
                schema: "tasks",
                table: "task_issues",
                columns: new[] { "StudioId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_task_labels_StudioId",
                schema: "tasks",
                table: "task_labels",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_task_labels_StudioId_Name",
                schema: "tasks",
                table: "task_labels",
                columns: new[] { "StudioId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_milestones_StudioId",
                schema: "tasks",
                table: "task_milestones",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_task_milestones_StudioId_Title",
                schema: "tasks",
                table: "task_milestones",
                columns: new[] { "StudioId", "Title" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_assignees",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_issue_labels",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_issues",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_labels",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_milestones",
                schema: "tasks");
        }
    }
}

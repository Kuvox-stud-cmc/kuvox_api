using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Tasks.Repositories.Migrations
{
    [DbContext(typeof(TasksDbContext))]
    [Migration("20260705163000_AddTaskCollaboration")]
    public partial class AddTaskCollaboration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_activities",
                schema: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_activities_task_issues_TaskIssueId",
                        column: x => x.TaskIssueId,
                        principalSchema: "tasks",
                        principalTable: "task_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                schema: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_comments_task_issues_TaskIssueId",
                        column: x => x.TaskIssueId,
                        principalSchema: "tasks",
                        principalTable: "task_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_issues_ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues",
                column: "ParentTaskIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_ActorUserId",
                schema: "tasks",
                table: "task_activities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_StudioId",
                schema: "tasks",
                table: "task_activities",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_TaskIssueId",
                schema: "tasks",
                table: "task_activities",
                column: "TaskIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_AuthorUserId",
                schema: "tasks",
                table: "task_comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_StudioId",
                schema: "tasks",
                table: "task_comments",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TaskIssueId",
                schema: "tasks",
                table: "task_comments",
                column: "TaskIssueId");

            migrationBuilder.AddForeignKey(
                name: "FK_task_issues_task_issues_ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues",
                column: "ParentTaskIssueId",
                principalSchema: "tasks",
                principalTable: "task_issues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_task_issues_task_issues_ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues");

            migrationBuilder.DropTable(
                name: "task_activities",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_comments",
                schema: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_task_issues_ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues");

            migrationBuilder.DropColumn(
                name: "ParentTaskIssueId",
                schema: "tasks",
                table: "task_issues");
        }
    }
}

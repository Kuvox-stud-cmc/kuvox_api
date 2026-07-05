using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Tasks.Repositories.Migrations
{
    [DbContext(typeof(TasksDbContext))]
    [Migration("20260705190000_AddTaskReviewers")]
    public partial class AddTaskReviewers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_reviewers",
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
                    table.PrimaryKey("PK_task_reviewers", x => new { x.TaskIssueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_reviewers_task_issues_TaskIssueId",
                        column: x => x.TaskIssueId,
                        principalSchema: "tasks",
                        principalTable: "task_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_reviewers_UserId",
                schema: "tasks",
                table: "task_reviewers",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_reviewers",
                schema: "tasks");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Auth.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioControlInvitationsAuditSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedEmailDomains",
                schema: "auth",
                table: "studios",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                schema: "auth",
                table: "studios",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "auth",
                table: "studios",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvitationExpiryDays",
                schema: "auth",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnInvites",
                schema: "auth",
                table: "studios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnMedia",
                schema: "auth",
                table: "studios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnMembers",
                schema: "auth",
                table: "studios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnProjects",
                schema: "auth",
                table: "studios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicSlug",
                schema: "auth",
                table: "studios",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""UPDATE auth.user_studios SET "Role" = 'Member' WHERE "Role" = 'User';""");

            migrationBuilder.CreateTable(
                name: "audit_log_entries",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkspaceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "studio_invitations",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_studio_invitations_studios_StudioId",
                        column: x => x.StudioId,
                        principalSchema: "auth",
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_studio_invitations_users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_studios_PublicSlug",
                schema: "auth",
                table: "studios",
                column: "PublicSlug",
                unique: true,
                filter: "\"PublicSlug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_Category",
                schema: "auth",
                table: "audit_log_entries",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_WorkspaceId_CreatedAt",
                schema: "auth",
                table: "audit_log_entries",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_studio_invitations_InvitedByUserId",
                schema: "auth",
                table: "studio_invitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_studio_invitations_StudioId_Email_Status",
                schema: "auth",
                table: "studio_invitations",
                columns: new[] { "StudioId", "Email", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_studio_invitations_TokenHash",
                schema: "auth",
                table: "studio_invitations",
                column: "TokenHash",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log_entries",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "studio_invitations",
                schema: "auth");

            migrationBuilder.DropIndex(
                name: "IX_studios_PublicSlug",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "AllowedEmailDomains",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "InvitationExpiryDays",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "NotifyOnInvites",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "NotifyOnMedia",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "NotifyOnMembers",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "NotifyOnProjects",
                schema: "auth",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "PublicSlug",
                schema: "auth",
                table: "studios");

        }
    }
}

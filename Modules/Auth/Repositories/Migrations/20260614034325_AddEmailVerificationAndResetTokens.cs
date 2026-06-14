using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Auth.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationAndResetTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAt",
                schema: "auth",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_tokens",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_tokens_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_TokenHash",
                schema: "auth",
                table: "auth_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_UserId_Purpose",
                schema: "auth",
                table: "auth_tokens",
                columns: new[] { "UserId", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_tokens",
                schema: "auth");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                schema: "auth",
                table: "users");
        }
    }
}

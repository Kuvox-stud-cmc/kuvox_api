using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Auth.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleActiveSessionGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId",
                schema: "auth",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSessionId",
                schema: "auth",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                schema: "auth",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE auth.refresh_tokens
                SET "RevokedAt" = NOW(), "UpdatedAt" = NOW()
                WHERE "RevokedAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_users_ActiveSessionId",
                schema: "auth",
                table: "users",
                column: "ActiveSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_SessionId_RevokedAt_ExpiresAt",
                schema: "auth",
                table: "refresh_tokens",
                columns: new[] { "UserId", "SessionId", "RevokedAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_ActiveSessionId",
                schema: "auth",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId_SessionId_RevokedAt_ExpiresAt",
                schema: "auth",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "ActiveSessionId",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SessionId",
                schema: "auth",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                schema: "auth",
                table: "refresh_tokens",
                column: "UserId");
        }
    }
}

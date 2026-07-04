using System;
using Kuvox.Api.Modules.Media.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    [DbContext(typeof(MediaDbContext))]
    [Migration("20260704143000_AddAlbumWorkspaceOwnership")]
    public partial class AddAlbumWorkspaceOwnership : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "media",
                table: "albums",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerKind",
                schema: "media",
                table: "albums",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                WITH ranked_owners AS (
                    SELECT
                        au."AlbumId",
                        au."UserId",
                        row_number() OVER (
                            PARTITION BY au."AlbumId"
                            ORDER BY CASE WHEN au."Role" = 'Owner' THEN 0 ELSE 1 END, au."CreatedAt", au."UserId"
                        ) AS rn
                    FROM media.album_users au
                )
                UPDATE media.albums a
                SET
                    "OwnerKind" = 'User',
                    "OwnerId" = ro."UserId"
                FROM ranked_owners ro
                WHERE ro."AlbumId" = a."Id"
                    AND ro.rn = 1;
                """);

            migrationBuilder.Sql("""
                UPDATE media.albums
                SET
                    "OwnerKind" = 'User',
                    "OwnerId" = '00000000-0000-0000-0000-000000000000'
                WHERE "OwnerKind" IS NULL OR "OwnerId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                schema: "media",
                table: "albums",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerKind",
                schema: "media",
                table: "albums",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_albums_OwnerKind_OwnerId",
                schema: "media",
                table: "albums",
                columns: new[] { "OwnerKind", "OwnerId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_albums_OwnerKind_OwnerId",
                schema: "media",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "media",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                schema: "media",
                table: "albums");
        }
    }
}

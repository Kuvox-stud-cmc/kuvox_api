using Kuvox.Api.Modules.Media.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    [DbContext(typeof(MediaDbContext))]
    [Migration("20260719090000_AddMediaSearchRevision")]
    public partial class AddMediaSearchRevision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "videos", "audios", "photos" })
            {
                migrationBuilder.AddColumn<long>(
                    name: "SearchRevision",
                    schema: "media",
                    table: table,
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L);

                migrationBuilder.Sql(
                    $"UPDATE media.\"{table}\" SET \"SearchRevision\" = 1 WHERE \"Status\" = 'Ready' AND \"SearchRevision\" = 0;");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "videos", "audios", "photos" })
            {
                migrationBuilder.DropColumn(
                    name: "SearchRevision",
                    schema: "media",
                    table: table);
            }
        }
    }
}

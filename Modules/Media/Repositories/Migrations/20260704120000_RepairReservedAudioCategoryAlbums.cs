using Kuvox.Api.Modules.Media.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuvox.Api.Modules.Media.Repositories.Migrations
{
    [DbContext(typeof(MediaDbContext))]
    [Migration("20260704120000_RepairReservedAudioCategoryAlbums")]
    public partial class RepairReservedAudioCategoryAlbums : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH categories("Category", "Name", "Description", "MaterialSymbol") AS (
                    VALUES
                        ('music', 'Music', 'Default Audio Album - Music', 'music_note'),
                        ('sfx', 'Sound Effects', 'Default Audio Album - Sound Effects', 'music_cast'),
                        ('voiceover', 'Voiceover', 'Default Audio Album - Voiceover', 'record_voice_over')
                ),
                users AS (
                    SELECT DISTINCT "UserId" FROM media.album_users
                    UNION
                    SELECT DISTINCT "UserId" FROM media.audio_users
                    UNION
                    SELECT DISTINCT "OwnerId" FROM media.audios WHERE "OwnerKind" = 'User'
                    UNION
                    SELECT DISTINCT "OwnerId" FROM media.photos WHERE "OwnerKind" = 'User'
                    UNION
                    SELECT DISTINCT "OwnerId" FROM media.videos WHERE "OwnerKind" = 'User'
                ),
                existing_system AS (
                    SELECT
                        au."UserId",
                        CASE regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            WHEN 'music' THEN 'music'
                            WHEN 'soundeffect' THEN 'sfx'
                            WHEN 'soundeffects' THEN 'sfx'
                            WHEN 'sfx' THEN 'sfx'
                            WHEN 'voiceover' THEN 'voiceover'
                            WHEN 'voiceovers' THEN 'voiceover'
                        END AS "Category",
                        row_number() OVER (
                            PARTITION BY au."UserId",
                                CASE regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                                    WHEN 'music' THEN 'music'
                                    WHEN 'soundeffect' THEN 'sfx'
                                    WHEN 'soundeffects' THEN 'sfx'
                                    WHEN 'sfx' THEN 'sfx'
                                    WHEN 'voiceover' THEN 'voiceover'
                                    WHEN 'voiceovers' THEN 'voiceover'
                                END
                            ORDER BY a."CreatedAt", a."Id"
                        ) AS rn
                    FROM media.album_users au
                    JOIN media.albums a ON a."Id" = au."AlbumId"
                    WHERE a."Kind" = 'Audio'
                        AND a."IsDeleteAble" = false
                        AND regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            IN ('music', 'soundeffect', 'soundeffects', 'sfx', 'voiceover', 'voiceovers')
                ),
                missing AS (
                    SELECT
                        u."UserId",
                        c."Category",
                        c."Name",
                        c."Description",
                        c."MaterialSymbol",
                        md5(u."UserId"::text || ':reserved-audio:' || c."Category")::uuid AS "AlbumId"
                    FROM users u
                    CROSS JOIN categories c
                    LEFT JOIN existing_system e
                        ON e."UserId" = u."UserId"
                        AND e."Category" = c."Category"
                        AND e.rn = 1
                    WHERE e."UserId" IS NULL
                ),
                inserted_albums AS (
                    INSERT INTO media.albums (
                        "Id",
                        "Name",
                        "Description",
                        "Kind",
                        "MaterialSymbol",
                        "IsDeleteAble",
                        "CreatedAt",
                        "UpdatedAt"
                    )
                    SELECT
                        "AlbumId",
                        "Name",
                        "Description",
                        'Audio',
                        "MaterialSymbol",
                        false,
                        now(),
                        now()
                    FROM missing
                    ON CONFLICT ("Id") DO NOTHING
                    RETURNING "Id"
                )
                INSERT INTO media.album_users (
                    "AlbumId",
                    "UserId",
                    "Role",
                    "IsFavorite",
                    "CreatedAt",
                    "UpdatedAt"
                )
                SELECT
                    "AlbumId",
                    "UserId",
                    'Owner',
                    false,
                    now(),
                    now()
                FROM missing
                ON CONFLICT ("AlbumId", "UserId") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                WITH user_category_albums AS (
                    SELECT
                        au."UserId",
                        a."Id" AS "AlbumId",
                        CASE regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            WHEN 'music' THEN 'music'
                            WHEN 'soundeffect' THEN 'sfx'
                            WHEN 'soundeffects' THEN 'sfx'
                            WHEN 'sfx' THEN 'sfx'
                            WHEN 'voiceover' THEN 'voiceover'
                            WHEN 'voiceovers' THEN 'voiceover'
                        END AS "Category",
                        a."IsDeleteAble",
                        a."CreatedAt"
                    FROM media.album_users au
                    JOIN media.albums a ON a."Id" = au."AlbumId"
                    WHERE a."Kind" = 'Audio'
                        AND regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            IN ('music', 'soundeffect', 'soundeffects', 'sfx', 'voiceover', 'voiceovers')
                ),
                canonical AS (
                    SELECT "UserId", "Category", "AlbumId"
                    FROM (
                        SELECT
                            *,
                            row_number() OVER (
                                PARTITION BY "UserId", "Category"
                                ORDER BY CASE WHEN "IsDeleteAble" = false THEN 0 ELSE 1 END, "CreatedAt", "AlbumId"
                            ) AS rn
                        FROM user_category_albums
                    ) ranked
                    WHERE rn = 1
                ),
                source_albums AS (
                    SELECT DISTINCT uca."UserId", uca."Category", uca."AlbumId", c."AlbumId" AS "CanonicalAlbumId"
                    FROM user_category_albums uca
                    JOIN canonical c
                        ON c."UserId" = uca."UserId"
                        AND c."Category" = uca."Category"
                    WHERE uca."AlbumId" <> c."AlbumId"
                )
                INSERT INTO media.album_audios (
                    "AlbumId",
                    "MediaId",
                    "CreatedAt",
                    "UpdatedAt"
                )
                SELECT DISTINCT
                    s."CanonicalAlbumId",
                    aa."MediaId",
                    now(),
                    now()
                FROM source_albums s
                JOIN media.album_audios aa ON aa."AlbumId" = s."AlbumId"
                ON CONFLICT ("AlbumId", "MediaId") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                WITH user_category_albums AS (
                    SELECT
                        au."UserId",
                        a."Id" AS "AlbumId",
                        CASE regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            WHEN 'music' THEN 'music'
                            WHEN 'soundeffect' THEN 'sfx'
                            WHEN 'soundeffects' THEN 'sfx'
                            WHEN 'sfx' THEN 'sfx'
                            WHEN 'voiceover' THEN 'voiceover'
                            WHEN 'voiceovers' THEN 'voiceover'
                        END AS "Category",
                        a."IsDeleteAble",
                        a."CreatedAt"
                    FROM media.album_users au
                    JOIN media.albums a ON a."Id" = au."AlbumId"
                    WHERE a."Kind" = 'Audio'
                        AND regexp_replace(lower(a."Name"), '[^a-z0-9]+', '', 'g')
                            IN ('music', 'soundeffect', 'soundeffects', 'sfx', 'voiceover', 'voiceovers')
                ),
                canonical AS (
                    SELECT "UserId", "Category", "AlbumId"
                    FROM (
                        SELECT
                            *,
                            row_number() OVER (
                                PARTITION BY "UserId", "Category"
                                ORDER BY CASE WHEN "IsDeleteAble" = false THEN 0 ELSE 1 END, "CreatedAt", "AlbumId"
                            ) AS rn
                        FROM user_category_albums
                    ) ranked
                    WHERE rn = 1
                ),
                source_albums AS (
                    SELECT DISTINCT uca."AlbumId"
                    FROM user_category_albums uca
                    JOIN canonical c
                        ON c."UserId" = uca."UserId"
                        AND c."Category" = uca."Category"
                    WHERE uca."AlbumId" <> c."AlbumId"
                        AND NOT EXISTS (
                            SELECT 1
                            FROM canonical keep
                            WHERE keep."AlbumId" = uca."AlbumId"
                        )
                )
                DELETE FROM media.albums a
                USING source_albums s
                WHERE a."Id" = s."AlbumId";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Gives event images the metadata a gallery needs: an explicit cover, alt text, a decorative
    /// flag, and an update timestamp.
    /// </summary>
    /// <remarks>
    /// The cover backfill preserves what every client already does — <c>imageUrls[0]</c> is the
    /// cover — by promoting the lowest <c>SortOrder</c> row per event. Ties on SortOrder are
    /// broken by Id so the statement is deterministic; without that the partial unique index
    /// below could reject the backfill on an event whose rows share a SortOrder.
    ///
    /// The index is partial rather than a plain unique constraint because only rows with
    /// <c>IsCover</c> participate: an event has many non-cover images and exactly one cover.
    /// Application code cannot enforce that across every write path, and a second cover is close
    /// to undetectable after the fact — every read path silently takes the first one it finds.
    ///
    /// Alt text is deliberately NOT constrained here. Existing rows have none, and requiring it
    /// at the database level would mean either rejecting them or backfilling a value no person
    /// wrote. The requirement is enforced on write instead, and <c>NeedsAltText</c> surfaces the
    /// legacy gap in the editor.
    /// </remarks>
    [DbContext(typeof(AppDatabaseContext))]
    [Migration("20260915120000_eventimagegallery")]
    public partial class EventImageGallery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCover",
                table: "EventImages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "EventImages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDecorative",
                table: "EventImages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Seeded from CreatedAt rather than now(), so an untouched image does not read as
            // having just been edited.
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "EventImages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql(
                """
                UPDATE "EventImages" SET "UpdatedAt" = "CreatedAt";
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE "EventImages" AS target
                SET "IsCover" = TRUE
                FROM (
                    SELECT DISTINCT ON ("EventId") "Id"
                    FROM "EventImages"
                    ORDER BY "EventId", "SortOrder", "Id"
                ) AS first_image
                WHERE target."Id" = first_image."Id";
                """
            );

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "UX_EventImages_EventId_Cover"
                ON "EventImages" ("EventId")
                WHERE "IsCover";
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_EventImages_EventId_Cover";
                """
            );

            migrationBuilder.DropColumn(name: "UpdatedAt", table: "EventImages");
            migrationBuilder.DropColumn(name: "IsDecorative", table: "EventImages");
            migrationBuilder.DropColumn(name: "AltText", table: "EventImages");
            migrationBuilder.DropColumn(name: "IsCover", table: "EventImages");
        }
    }
}

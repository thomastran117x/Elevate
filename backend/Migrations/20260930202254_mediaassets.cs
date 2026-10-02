using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Adds <c>MediaAssets</c>, the lifecycle record for an uploaded image: presigned uploads now
    /// land in a private quarantine container, and an image reaches the public container only once
    /// its asset is <c>Ready</c>. Existing image URLs are backfilled as legacy assets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Status and Origin are strings, not ints.</b> The table exists to answer "where is this
    /// upload stuck?", and that question gets asked in psql during an incident. A row reading
    /// <c>Processing</c> answers it; a row reading <c>2</c> sends someone to the source to find out
    /// what 2 means, and silently means something else if the enum is ever reordered. The cost is
    /// a few bytes per row on a table with one row per image.
    /// </para>
    /// <para>
    /// <b>No foreign key from <c>EventImages.ImageUrl</c> (or the club and user image columns) to
    /// this table yet.</b> <c>ImageUrl</c> is written by six code paths, and club and user images
    /// are plain string columns with no key to point anywhere. A required reference on day one
    /// turns every write path this change missed into a 500 in production. The plan is a nullable
    /// <c>MediaAssetId</c> column in a later migration, a backfill by URL match, a check that it
    /// reached 100%, and only then the constraint. Until then the relationship is by URL. The
    /// orphan sweeper treats the <c>PublicUrl</c> of an upload still in flight as a reference; a
    /// settled asset's blob is reclaimed like any other once no owning column points at it.
    /// </para>
    /// <para>
    /// The foreign keys this table does carry (owner, club, event) are <c>SET NULL</c>: an asset
    /// outlives the account or event it was uploaded for, because the same image can still be
    /// live elsewhere — a recurrence occurrence, or a club the uploader was staff of.
    /// </para>
    /// <para>
    /// <b>The legacy backfill</b> creates one <c>Ready</c>, <c>Legacy</c> asset per distinct,
    /// non-blank URL in every column the orphan sweeper treats as a reference: <c>Users.Avatar</c>,
    /// <c>Clubs.ClubImage</c>, <c>Clubs.BannerImage</c>, <c>Clubs.GalleryImages</c>,
    /// <c>ClubVersions.ClubImage</c> and <c>EventImages.ImageUrl</c>. It does <b>not</b>
    /// re-validate anything: the bytes behind those URLs were uploaded under the old rules, still
    /// carry whatever EXIF they had, and keep working exactly as before. <c>ValidatedAt</c> is left
    /// null on purpose, so <c>WHERE "Origin" = 'Legacy' AND "ValidatedAt" IS NULL</c> is the
    /// backlog a later backfill re-checks.
    /// </para>
    /// <para>
    /// It also does not filter to URLs in our own container. An account that signed in with an
    /// external provider can have that provider's picture URL as its avatar, and those rows are
    /// included too. They are harmless here — nothing serves them through this table — but
    /// anything that processes the legacy backlog must check a URL is ours before fetching it.
    /// Owner, club and event are left null: which user uploaded a legacy image was never recorded,
    /// and one URL can be shared by several events.
    /// </para>
    /// </remarks>
    public partial class mediaassets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: true),
                    ClubId = table.Column<int>(type: "integer", nullable: true),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuarantineBlobPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PublicUrl = table.Column<string>(type: "text", nullable: true),
                    DeclaredContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    ByteSize = table.Column<long>(type: "bigint", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ClubId",
                table: "MediaAssets",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_EventId",
                table: "MediaAssets",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_OwnerUserId",
                table: "MediaAssets",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PublicId",
                table: "MediaAssets",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PublicUrl",
                table: "MediaAssets",
                column: "PublicUrl",
                unique: true,
                filter: "\"PublicUrl\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_QuarantineBlobPath",
                table: "MediaAssets",
                column: "QuarantineBlobPath",
                filter: "\"QuarantineBlobPath\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Status_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "Status", "CreatedAt" });

            // UNION rather than UNION ALL: the unique index on PublicUrl needs each URL once, and
            // the same URL legitimately appears in several places (a club image and every version
            // snapshot of it; one image shared by a series' occurrences).
            migrationBuilder.Sql(
                """
                INSERT INTO "MediaAssets"
                    ("PublicId", "Status", "Origin", "PublicUrl", "DeclaredContentType",
                     "CreatedAt", "UpdatedAt", "AttemptCount")
                SELECT gen_random_uuid(), 'Ready', 'Legacy', urls.url, 'application/octet-stream',
                       now(), now(), 0
                FROM (
                    SELECT "Avatar" AS url FROM "Users"
                    UNION
                    SELECT "ClubImage" FROM "Clubs"
                    UNION
                    SELECT "BannerImage" FROM "Clubs"
                    UNION
                    SELECT json_array_elements_text("GalleryImages") FROM "Clubs"
                    WHERE "GalleryImages" IS NOT NULL AND json_typeof("GalleryImages") = 'array'
                    UNION
                    SELECT "ClubImage" FROM "ClubVersions"
                    UNION
                    SELECT "ImageUrl" FROM "EventImages"
                ) AS urls
                WHERE urls.url IS NOT NULL AND btrim(urls.url) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}

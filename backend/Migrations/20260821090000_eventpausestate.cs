using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Adds the bookkeeping behind the reversible event lifecycle: which state an event came
    /// from and when it moved, so the most recent change can be undone in one click.
    /// </summary>
    /// <remarks>
    /// The new <c>Paused</c> lifecycle state itself needs no schema change — the column is a
    /// plain integer with no check constraint, and Paused was appended as ordinal 4 so every
    /// stored value keeps its meaning.
    /// </remarks>
    [DbContext(typeof(AppDatabaseContext))]
    [Migration("20260821090000_eventpausestate")]
    public partial class EventPauseState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviousLifecycleState",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LifecycleChangedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill from the audit trail so existing events can answer "when did this last
            // change state?" immediately. PreviousLifecycleState is deliberately left null: the
            // undo window for these has long since passed, and inventing one would offer an
            // organizer an "undo" for something they did months ago.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_name = 'EventVersions'
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot backfill LifecycleChangedAt: the EventVersions table is missing.';
                    END IF;
                END $$;

                UPDATE "Events" e
                SET "LifecycleChangedAt" = latest."CreatedAt"
                FROM (
                    SELECT DISTINCT ON (v."EventId")
                           v."EventId",
                           v."CreatedAt"
                    FROM "EventVersions" v
                    WHERE v."ActionType" IN ('publish', 'cancel', 'archive')
                    ORDER BY v."EventId", v."VersionNumber" DESC
                ) AS latest
                WHERE latest."EventId" = e."Id";

                -- Events that never transitioned (drafts, and rows predating versioning) fall
                -- back to when they were last touched.
                UPDATE "Events"
                SET "LifecycleChangedAt" = "UpdatedAt"
                WHERE "LifecycleChangedAt" IS NULL;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_Events_LifecycleState",
                table: "Events",
                column: "LifecycleState");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_LifecycleState",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "LifecycleChangedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PreviousLifecycleState",
                table: "Events");
        }
    }
}

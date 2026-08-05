using System;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEventRecurrenceSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OccurrenceIndex",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeriesOverridden",
                table: "Events",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Events",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    TemplateEventId = table.Column<int>(type: "int", nullable: true),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    ByWeekdayMask = table.Column<int>(type: "int", nullable: false),
                    MonthlyDayPolicy = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstOccurrenceLocalStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    EndMode = table.Column<int>(type: "int", nullable: false),
                    EndLocalDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: true),
                    GeneratedCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSeries_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Events_SeriesId_OccurrenceIndex",
                table: "Events",
                columns: new[] { "SeriesId", "OccurrenceIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_SeriesId_StartTime",
                table: "Events",
                columns: new[] { "SeriesId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_EventSeries_ClubId",
                table: "EventSeries",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSeries_TemplateEventId",
                table: "EventSeries",
                column: "TemplateEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_EventSeries_SeriesId",
                table: "Events",
                column: "SeriesId",
                principalTable: "EventSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_EventSeries_SeriesId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "EventSeries");

            migrationBuilder.DropIndex(
                name: "IX_Events_SeriesId_OccurrenceIndex",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_SeriesId_StartTime",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "OccurrenceIndex",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SeriesOverridden",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Events");
        }
    }
}

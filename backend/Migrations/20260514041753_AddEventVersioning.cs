using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEventVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "EventVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedFieldsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ActorRole = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollbackSourceVersionNumber = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventVersions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EventVersions_CreatedAt",
                table: "EventVersions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EventVersions_EventId_VersionNumber",
                table: "EventVersions",
                columns: new[] { "EventId", "VersionNumber" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE `Events`
                SET `CurrentVersionNumber` = 1
                WHERE `CurrentVersionNumber` = 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `EventVersions`
                (
                    `EventId`,
                    `VersionNumber`,
                    `ActionType`,
                    `SnapshotJson`,
                    `ChangedFieldsJson`,
                    `ActorUserId`,
                    `ActorRole`,
                    `RollbackSourceVersionNumber`,
                    `CreatedAt`
                )
                SELECT
                    `Events`.`Id`,
                    1,
                    'create',
                    JSON_OBJECT(
                        'Name', `Events`.`Name`,
                        'Description', `Events`.`Description`,
                        'Location', `Events`.`Location`,
                        'IsPrivate', `Events`.`isPrivate`,
                        'MaxParticipants', `Events`.`maxParticipants`,
                        'RegisterCost', `Events`.`registerCost`,
                        'StartTime', `Events`.`StartTime`,
                        'EndTime', `Events`.`EndTime`,
                        'ClubId', `Events`.`ClubId`,
                        'Category', CASE `Events`.`Category`
                            WHEN 0 THEN 'Sports'
                            WHEN 1 THEN 'Music'
                            WHEN 2 THEN 'Academic'
                            WHEN 3 THEN 'Workshop'
                            WHEN 4 THEN 'Conference'
                            WHEN 5 THEN 'Social'
                            WHEN 6 THEN 'Cultural'
                            WHEN 7 THEN 'Gaming'
                            WHEN 8 THEN 'Food'
                            WHEN 9 THEN 'Fitness'
                            WHEN 10 THEN 'Networking'
                            WHEN 11 THEN 'Volunteer'
                            WHEN 12 THEN 'Party'
                            WHEN 13 THEN 'Arts'
                            ELSE 'Other'
                        END,
                        'VenueName', `Events`.`VenueName`,
                        'City', `Events`.`City`,
                        'Latitude', `Events`.`Latitude`,
                        'Longitude', `Events`.`Longitude`,
                        'Tags', COALESCE(`Events`.`Tags`, JSON_ARRAY())
                    ),
                    JSON_ARRAY(),
                    `Clubs`.`UserId`,
                    'System',
                    NULL,
                    `Events`.`CreatedAt`
                FROM `Events`
                INNER JOIN `Clubs` ON `Clubs`.`Id` = `Events`.`ClubId`
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM `EventVersions`
                    WHERE `EventVersions`.`EventId` = `Events`.`Id`
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Events");
        }
    }
}

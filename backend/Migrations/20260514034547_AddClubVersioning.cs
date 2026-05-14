using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddClubVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "Clubs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ClubVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedFieldsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClubImage = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ActorRole = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollbackSourceVersionNumber = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubVersions_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClubVersions_ClubId_VersionNumber",
                table: "ClubVersions",
                columns: new[] { "ClubId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubVersions_ClubImage",
                table: "ClubVersions",
                column: "ClubImage");

            migrationBuilder.CreateIndex(
                name: "IX_ClubVersions_CreatedAt",
                table: "ClubVersions",
                column: "CreatedAt");

            migrationBuilder.Sql("""
                UPDATE `Clubs`
                SET `CurrentVersionNumber` = 1
                WHERE `CurrentVersionNumber` = 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `ClubVersions`
                (
                    `ClubId`,
                    `VersionNumber`,
                    `ActionType`,
                    `SnapshotJson`,
                    `ChangedFieldsJson`,
                    `ClubImage`,
                    `ActorUserId`,
                    `ActorRole`,
                    `RollbackSourceVersionNumber`,
                    `CreatedAt`
                )
                SELECT
                    `Id`,
                    1,
                    'create',
                    JSON_OBJECT(
                        'Name', `Name`,
                        'Description', `Description`,
                        'Clubtype', CASE `Clubtype`
                            WHEN 0 THEN 'Sports'
                            WHEN 1 THEN 'Academic'
                            WHEN 2 THEN 'Social'
                            WHEN 3 THEN 'Cultural'
                            WHEN 4 THEN 'Gaming'
                            ELSE 'Other'
                        END,
                        'ClubImage', `ClubImage`,
                        'Phone', `Phone`,
                        'Email', `Email`,
                        'WebsiteUrl', `WebsiteUrl`,
                        'Location', `Location`,
                        'MaxMemberCount', `MaxMemberCount`,
                        'IsPrivate', `isPrivate`
                    ),
                    JSON_ARRAY(),
                    `ClubImage`,
                    `UserId`,
                    'System',
                    NULL,
                    `CreatedAt`
                FROM `Clubs`
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM `ClubVersions`
                    WHERE `ClubVersions`.`ClubId` = `Clubs`.`Id`
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Clubs");
        }
    }
}

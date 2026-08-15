using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    [DbContext(typeof(AppDatabaseContext))]
    [Migration("20260815170000_usernamechangecooldown")]
    public partial class UsernameChangeCooldown : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Users"
                        WHERE "Username" IS NOT NULL
                          AND btrim("Username") = ''
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot normalize usernames: one or more stored usernames are empty after trimming.';
                    END IF;

                    IF EXISTS (
                        SELECT lower(btrim("Username"))
                        FROM "Users"
                        WHERE "Username" IS NOT NULL
                        GROUP BY lower(btrim("Username"))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot normalize usernames: trimming and lowercasing would create duplicates.';
                    END IF;
                END $$;

                UPDATE "Users"
                SET "Username" = lower(btrim("Username"))
                WHERE "Username" IS NOT NULL;
                """
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "UsernameChangeAvailableAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UsernameReservations",
                columns: table => new
                {
                    Username = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ReservedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsernameReservations", x => x.Username);
                    table.ForeignKey(
                        name: "FK_UsernameReservations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsernameReservations_ReservedUntilUtc",
                table: "UsernameReservations",
                column: "ReservedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsernameReservations_UserId",
                table: "UsernameReservations",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UsernameReservations");

            migrationBuilder.DropColumn(
                name: "UsernameChangeAvailableAtUtc",
                table: "Users");
        }
    }
}

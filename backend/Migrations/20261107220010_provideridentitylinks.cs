using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class provideridentitylinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserIdentityLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentityLinks_Provider_Subject",
                table: "UserIdentityLinks",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentityLinks_UserId_Provider",
                table: "UserIdentityLinks",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            // Carry every existing link across before the columns go. When each link was made was
            // never recorded, so the account's creation time stands in for it; the email is the
            // account's own, which is the address the email-fallback link was made on.
            migrationBuilder.Sql("""
                INSERT INTO "UserIdentityLinks" ("UserId", "Provider", "Subject", "Email", "DisplayName", "LinkedAtUtc", "LastUsedAtUtc")
                SELECT "Id", 'google', "GoogleID", "Email", "Name", "CreatedAt", NULL
                FROM "Users"
                WHERE "GoogleID" IS NOT NULL AND btrim("GoogleID") <> '';

                INSERT INTO "UserIdentityLinks" ("UserId", "Provider", "Subject", "Email", "DisplayName", "LinkedAtUtc", "LastUsedAtUtc")
                SELECT "Id", 'microsoft', "MicrosoftID", "Email", "Name", "CreatedAt", NULL
                FROM "Users"
                WHERE "MicrosoftID" IS NOT NULL AND btrim("MicrosoftID") <> '';
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_GoogleID",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_MicrosoftID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MicrosoftID",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleID",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MicrosoftID",
                table: "Users",
                type: "text",
                nullable: true);

            // One link per provider per account is a unique index, so each account restores at
            // most one value per column.
            migrationBuilder.Sql("""
                UPDATE "Users" AS u
                SET "GoogleID" = l."Subject"
                FROM "UserIdentityLinks" AS l
                WHERE l."UserId" = u."Id" AND l."Provider" = 'google';

                UPDATE "Users" AS u
                SET "MicrosoftID" = l."Subject"
                FROM "UserIdentityLinks" AS l
                WHERE l."UserId" = u."Id" AND l."Provider" = 'microsoft';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleID",
                table: "Users",
                column: "GoogleID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_MicrosoftID",
                table: "Users",
                column: "MicrosoftID",
                unique: true);

            migrationBuilder.DropTable(
                name: "UserIdentityLinks");
        }
    }
}

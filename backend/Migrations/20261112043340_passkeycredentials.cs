using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class passkeycredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "PasskeyUserHandle",
                table: "Users",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PasskeyCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1023, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    UserHandle = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    AaGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    Transports = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AttestationFormat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsBackupEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    FriendlyName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CounterAnomalyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasskeyCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasskeyCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_PasskeyUserHandle",
                table: "Users",
                column: "PasskeyUserHandle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_CredentialId",
                table: "PasskeyCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_UserId",
                table: "PasskeyCredentials",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasskeyCredentials");

            migrationBuilder.DropIndex(
                name: "IX_Users_PasskeyUserHandle",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasskeyUserHandle",
                table: "Users");
        }
    }
}

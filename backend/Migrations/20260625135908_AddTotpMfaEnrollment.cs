using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTotpMfaEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TotpMfaEnrollments",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EncryptionKeyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsTotpMfaEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    EnrolledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpMfaEnrollments", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TotpMfaEnrollments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TotpMfaEnrollments");
        }
    }
}

using System;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEventWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WaitlistCount",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WaitlistEnabled",
                table: "Events",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EventWaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PromotedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeftAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PromotionEmailQueuedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DietaryNeeds = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventWaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventWaitlistEntries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventWaitlistEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EventWaitlistEntries_EventId_Status_JoinedAtUtc_Id",
                table: "EventWaitlistEntries",
                columns: new[] { "EventId", "Status", "JoinedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EventWaitlistEntries_EventId_UserId",
                table: "EventWaitlistEntries",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventWaitlistEntries_UserId_Status",
                table: "EventWaitlistEntries",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventWaitlistEntries");

            migrationBuilder.DropColumn(
                name: "WaitlistCount",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "WaitlistEnabled",
                table: "Events");
        }
    }
}

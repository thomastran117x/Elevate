using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEventInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventInvitationLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: false),
                    RedemptionCount = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RevokedByUserId = table.Column<int>(type: "int", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventInvitationLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventInvitationLinks_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientEmailNormalized = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LifecycleStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimTokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EventInvitationLinkId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeclinedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    DeclinedByUserId = table.Column<int>(type: "int", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "int", nullable: true),
                    DeliveryError = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventInvitations_EventInvitationLinks_EventInvitationLinkId",
                        column: x => x.EventInvitationLinkId,
                        principalTable: "EventInvitationLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EventInvitations_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitationLinks_EventId_RevokedAtUtc_ExpiresAt",
                table: "EventInvitationLinks",
                columns: new[] { "EventId", "RevokedAtUtc", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitationLinks_TokenHash",
                table: "EventInvitationLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitations_ClaimTokenHash",
                table: "EventInvitations",
                column: "ClaimTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitations_EventId_LifecycleStatus",
                table: "EventInvitations",
                columns: new[] { "EventId", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitations_EventInvitationLinkId_RecipientUserId",
                table: "EventInvitations",
                columns: new[] { "EventInvitationLinkId", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitations_RecipientEmailNormalized_LifecycleStatus",
                table: "EventInvitations",
                columns: new[] { "RecipientEmailNormalized", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_EventInvitations_RecipientUserId_LifecycleStatus",
                table: "EventInvitations",
                columns: new[] { "RecipientUserId", "LifecycleStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventInvitations");

            migrationBuilder.DropTable(
                name: "EventInvitationLinks");
        }
    }
}

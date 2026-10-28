using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Makes the account security history durable end to end (#326 review).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>security_history_outbox</c></b> holds each entry from the moment the API records it
    /// until Debezium relays it to the security-history topic, so a broker outage or a restart no
    /// longer loses entries. It mirrors the search outboxes' columns, which the EventRouter
    /// expects. The security-history worker deletes rows after a day; Debezium has read them from
    /// the WAL by then and ignores deletes.
    /// </para>
    /// <para>
    /// <b>The <c>Alert*</c> columns</b> record whether an entry owes its owner an alert email and
    /// how delivery went. They are written in the same insert as the entry, so an alert cannot be
    /// lost once its entry is committed, and the worker's dispatcher advances them. Existing rows
    /// default to <c>None</c>: alerts already sent or lost before this change are not resent.
    /// The filtered index covers only pending alerts, the dispatcher's one query.
    /// </para>
    /// </remarks>
    public partial class securityhistorydurability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlertAttempts",
                table: "AccountSecurityEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AlertNextAttemptAtUtc",
                table: "AccountSecurityEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlertState",
                table: "AccountSecurityEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "security_history_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregatetype = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    aggregateid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    payload = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_history_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSecurityEvents_AlertNextAttemptAtUtc",
                table: "AccountSecurityEvents",
                column: "AlertNextAttemptAtUtc",
                filter: "\"AlertState\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_security_history_outbox_created_at",
                table: "security_history_outbox",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_history_outbox");

            migrationBuilder.DropIndex(
                name: "IX_AccountSecurityEvents_AlertNextAttemptAtUtc",
                table: "AccountSecurityEvents");

            migrationBuilder.DropColumn(
                name: "AlertAttempts",
                table: "AccountSecurityEvents");

            migrationBuilder.DropColumn(
                name: "AlertNextAttemptAtUtc",
                table: "AccountSecurityEvents");

            migrationBuilder.DropColumn(
                name: "AlertState",
                table: "AccountSecurityEvents");
        }
    }
}

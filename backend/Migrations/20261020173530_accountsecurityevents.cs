using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Adds <c>AccountSecurityEvents</c>, the account owner's 90-day security history (#326),
    /// written by the security-history worker from the auth security event contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Append only.</b> Nothing updates a row: the worker inserts, the retention purge deletes
    /// by <c>ExpiresAtUtc</c>, and the cascade from <c>Users</c> deletes an account's history with
    /// the account, the same hard delete its devices and MFA enrollments get.
    /// </para>
    /// <para>
    /// <b>The unique <c>MessageId</c></b> makes a redelivered Kafka message a no-op, and the
    /// descending <c>(UserId, OccurredAtUtc, Id)</c> index serves the only read, one account's
    /// newest entries first.
    /// </para>
    /// <para>
    /// Kind, Client and DeviceClass are strings for the same reason as <c>MediaAssets.Status</c>:
    /// a stored row must keep its meaning if an enum is ever reordered.
    /// </para>
    /// </remarks>
    public partial class accountsecurityevents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountSecurityEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    EventName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Factor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Reason = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    Client = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Network = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InitiatedByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSecurityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSecurityEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSecurityEvents_ExpiresAtUtc",
                table: "AccountSecurityEvents",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSecurityEvents_MessageId",
                table: "AccountSecurityEvents",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountSecurityEvents_UserId_OccurredAtUtc_Id",
                table: "AccountSecurityEvents",
                columns: new[] { "UserId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSecurityEvents");
        }
    }
}

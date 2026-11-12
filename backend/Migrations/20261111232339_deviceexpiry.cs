using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class deviceexpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing trust gets the 90 days it was granted, counted from when the device was
            // first trusted. A device older than that asks for verification once more.
            migrationBuilder.Sql(
                "UPDATE \"Devices\" SET \"ExpiresAt\" = \"CreatedAt\" + interval '90 days';");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ExpiresAt",
                table: "Devices",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_ExpiresAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Devices");
        }
    }
}

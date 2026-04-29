using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class HardenTrustedDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_UserId_DeviceType_ClientName",
                table: "Devices");

            migrationBuilder.AddColumn<string>(
                name: "DeviceTokenHash",
                table: "Devices",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("DELETE FROM `Devices`;");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceTokenHash",
                table: "Devices",
                column: "DeviceTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId_DeviceType_ClientName",
                table: "Devices",
                columns: new[] { "UserId", "DeviceType", "ClientName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_DeviceTokenHash",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Devices_UserId_DeviceType_ClientName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DeviceTokenHash",
                table: "Devices");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId_DeviceType_ClientName",
                table: "Devices",
                columns: new[] { "UserId", "DeviceType", "ClientName" },
                unique: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StonesHotspot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class FreeAccessAndSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IssuedToDeviceId",
                table: "Vouchers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Vouchers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Vouchers",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Staff"); // existing vouchers were all made by staff

            migrationBuilder.CreateTable(
                name: "SmsChallenges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsChallenges_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmsMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_IssuedToDeviceId",
                table: "Vouchers",
                column: "IssuedToDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Source_IssuedToDeviceId_CreatedAt",
                table: "Vouchers",
                columns: new[] { "Source", "IssuedToDeviceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsChallenges_DeviceId",
                table: "SmsChallenges",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsChallenges_Phone_CreatedAt",
                table: "SmsChallenges",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_CreatedAt",
                table: "SmsMessages",
                column: "CreatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Devices_IssuedToDeviceId",
                table: "Vouchers",
                column: "IssuedToDeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Devices_IssuedToDeviceId",
                table: "Vouchers");

            migrationBuilder.DropTable(
                name: "SmsChallenges");

            migrationBuilder.DropTable(
                name: "SmsMessages");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_IssuedToDeviceId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_Source_IssuedToDeviceId_CreatedAt",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "IssuedToDeviceId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Vouchers");
        }
    }
}

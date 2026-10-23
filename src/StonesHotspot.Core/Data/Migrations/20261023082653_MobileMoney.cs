using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StonesHotspot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class MobileMoney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MobilePayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TariffId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OperatorTransactionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoucherId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilePayments_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MobilePayments_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilePayments_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobilePayments_DeviceId",
                table: "MobilePayments",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePayments_Reference",
                table: "MobilePayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobilePayments_Status_CreatedAt",
                table: "MobilePayments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MobilePayments_TariffId",
                table: "MobilePayments",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilePayments_VoucherId",
                table: "MobilePayments",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobilePayments");
        }
    }
}

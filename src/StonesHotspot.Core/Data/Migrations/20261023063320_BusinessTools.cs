using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StonesHotspot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Transactions",
                type: "TEXT",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "TrackStock",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Products created before this column counted their stock.
            migrationBuilder.Sql("UPDATE Products SET TrackStock = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TrackStock",
                table: "Products");
        }
    }
}

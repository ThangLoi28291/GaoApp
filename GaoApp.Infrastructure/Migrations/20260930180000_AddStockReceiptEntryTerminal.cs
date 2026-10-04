using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReceiptEntryTerminal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntryTerminalCode",
                table: "StockDocument",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntryTerminalId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryTerminalName",
                table: "StockDocument",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryTerminalCode",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "EntryTerminalId",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "EntryTerminalName",
                table: "StockDocument");
        }
    }
}

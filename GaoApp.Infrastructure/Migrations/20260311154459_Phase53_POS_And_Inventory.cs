using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase53_POS_And_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowNegativeInventory",
                table: "Warehouses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "POSShifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceLineId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_WarehouseId",
                table: "POSShifts",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_POSShifts_WarehouseId",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "AllowNegativeInventory",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "ReferenceLineId",
                table: "InventoryTransactions");
        }
    }
}

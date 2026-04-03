using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase53_POS_And_barcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts");

            migrationBuilder.AddColumn<int>(
                name: "BarcodeSource",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "OrderLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitName",
                table: "OrderLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Multiplier",
                table: "OrderLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ScannedBarcode",
                table: "OrderLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellingUnitId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellingUnitName",
                table: "OrderLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_VariantId",
                table: "OrderLines",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_ProductVariant_VariantId",
                table: "OrderLines",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_ProductVariant_VariantId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_VariantId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BarcodeSource",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BaseUnitName",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Multiplier",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ScannedBarcode",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "SellingUnitId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "SellingUnitName",
                table: "OrderLines");

            migrationBuilder.AddForeignKey(
                name: "FK_POSShifts_Warehouses_WarehouseId",
                table: "POSShifts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

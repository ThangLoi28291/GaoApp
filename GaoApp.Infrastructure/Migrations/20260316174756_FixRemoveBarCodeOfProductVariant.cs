using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRemoveBarCodeOfProductVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsActive",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "ProductVariant");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "ProductVariant",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "ProductUnitConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "ProductUnitConversionId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsBaseUnit",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "IsBaseUnit" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsDefaultForSale",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "IsDefaultForSale" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId",
                principalTable: "ProductUnitConversion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsBaseUnit",
                table: "ProductUnitConversion");

            migrationBuilder.DropIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsDefaultForSale",
                table: "ProductUnitConversion");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "ProductVariant",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "ProductVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsActive",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "ProductUnitConversionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId",
                principalTable: "ProductUnitConversion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

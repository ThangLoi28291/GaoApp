using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_ProductVariant_Unique_Filter_IsDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true);
        }
    }
}

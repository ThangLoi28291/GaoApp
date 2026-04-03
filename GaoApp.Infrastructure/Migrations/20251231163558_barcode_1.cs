using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class barcode_1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}

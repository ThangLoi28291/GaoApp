using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixProductVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.AddColumn<string>(
                name: "ProductVariantName",
                table: "ProductVariant",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductVariantNameNormalized",
                table: "ProductVariant",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_ProductVariantName",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "ProductVariantName" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_ProductVariantNameNormalized",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "ProductVariantNameNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "Sku" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariant_Store_Product_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "ProductId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_Store_ProductVariantName",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_Store_ProductVariantNameNormalized",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_Store_Sku",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "UX_ProductVariant_Store_Product_Sku",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "ProductVariantName",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "ProductVariantNameNormalized",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true);
        }
    }
}

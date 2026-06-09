using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImprovePromotionManagementIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId",
                table: "PromotionComboRule");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StoreId_Type_IsActive_StartAtUtc_EndAtUtc_IsDeleted",
                table: "Promotions",
                columns: new[] { "StoreId", "Type", "IsActive", "StartAtUtc", "EndAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionItems",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionComboRule",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Promotions_StoreId_Type_IsActive_StartAtUtc_EndAtUtc_IsDeleted",
                table: "Promotions");

            migrationBuilder.DropIndex(
                name: "IX_PromotionItems_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionItems");

            migrationBuilder.DropIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionComboRule");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId",
                table: "PromotionComboRule",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId" });
        }
    }
}

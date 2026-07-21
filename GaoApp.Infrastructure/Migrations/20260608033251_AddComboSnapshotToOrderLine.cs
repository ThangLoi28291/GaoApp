using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComboSnapshotToOrderLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PromotionComboRule_Promotions_PromotionId",
                table: "PromotionComboRule");

            migrationBuilder.DropIndex(
                name: "IX_PromotionComboRule_StoreId",
                table: "PromotionComboRule");

            migrationBuilder.AlterColumn<decimal>(
                name: "RequiredQuantity",
                table: "PromotionComboRule",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<decimal>(
                name: "ComboAllocatedDiscount",
                table: "OrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ComboPromotionId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComboPromotionName",
                table: "OrderLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComboPromotionNote",
                table: "OrderLines",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId",
                table: "PromotionComboRule",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PromotionComboRule_Promotions_PromotionId",
                table: "PromotionComboRule",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PromotionComboRule_Promotions_PromotionId",
                table: "PromotionComboRule");

            migrationBuilder.DropIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId",
                table: "PromotionComboRule");

            migrationBuilder.DropColumn(
                name: "ComboAllocatedDiscount",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ComboPromotionId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ComboPromotionName",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ComboPromotionNote",
                table: "OrderLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "RequiredQuantity",
                table: "PromotionComboRule",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldDefaultValue: 1m);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_StoreId",
                table: "PromotionComboRule",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_PromotionComboRule_Promotions_PromotionId",
                table: "PromotionComboRule",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

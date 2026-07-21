using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyXGetYGiftLineFieldsToOrderLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiftPromotionId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GiftPromotionName",
                table: "OrderLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GiftPromotionNote",
                table: "OrderLines",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiftSourceLineId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPromotionGift",
                table: "OrderLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_GiftPromotionId",
                table: "OrderLines",
                columns: new[] { "StoreId", "GiftPromotionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_GiftSourceLineId",
                table: "OrderLines",
                columns: new[] { "StoreId", "GiftSourceLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_IsPromotionGift",
                table: "OrderLines",
                columns: new[] { "StoreId", "IsPromotionGift" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_GiftPromotionId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_GiftSourceLineId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_IsPromotionGift",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "GiftPromotionId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "GiftPromotionName",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "GiftPromotionNote",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "GiftSourceLineId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "IsPromotionGift",
                table: "OrderLines");
        }
    }
}

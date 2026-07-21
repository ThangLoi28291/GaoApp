using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "ProductVariantBarcodeHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_StoreId",
                table: "ProductVariantBarcodeHistory",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_Stores_StoreId",
                table: "ProductVariantBarcodeHistory",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_Stores_StoreId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_StoreId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "ProductVariantBarcodeHistory");
        }
    }
}

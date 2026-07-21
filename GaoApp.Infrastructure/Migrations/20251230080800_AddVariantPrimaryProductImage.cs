using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantPrimaryProductImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
          

            migrationBuilder.AddColumn<int>(
                name: "PrimaryProductImageId",
                table: "ProductVariant",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_PrimaryProductImageId",
                table: "ProductVariant",
                column: "PrimaryProductImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId" },
                unique: true,
                filter: "[IsPrimary] = 1 AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariant_ProductImages_PrimaryProductImageId",
                table: "ProductVariant",
                column: "PrimaryProductImageId",
                principalTable: "ProductImages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariant_ProductImages_PrimaryProductImageId",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_PrimaryProductImageId",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductImages_StoreId_ProductId",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "PrimaryProductImageId",
                table: "ProductVariant");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId" },
                unique: true,
                filter: "[IsPrimary] = 1");
        }
    }
}

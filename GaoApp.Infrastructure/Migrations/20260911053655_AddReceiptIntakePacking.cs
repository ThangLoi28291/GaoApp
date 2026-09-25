using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptIntakePacking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedBaseUnitName",
                table: "StockDocumentProvisionalItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedCategoryId",
                table: "StockDocumentProvisionalItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProposedFactor",
                table: "StockDocumentProvisionalItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedProductVariantId",
                table: "StockDocumentProvisionalItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems",
                column: "ProposedBaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_ProposedProductVariantId",
                table: "StockDocumentProvisionalItems",
                column: "ProposedProductVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocumentProvisionalItems_ProductVariant_ProposedProductVariantId",
                table: "StockDocumentProvisionalItems",
                column: "ProposedProductVariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocumentProvisionalItems_Unit_ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems",
                column: "ProposedBaseUnitId",
                principalTable: "Unit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockDocumentProvisionalItems_ProductVariant_ProposedProductVariantId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockDocumentProvisionalItems_Unit_ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropIndex(
                name: "IX_StockDocumentProvisionalItems_ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropIndex(
                name: "IX_StockDocumentProvisionalItems_ProposedProductVariantId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropColumn(
                name: "ProposedBaseUnitId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropColumn(
                name: "ProposedBaseUnitName",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropColumn(
                name: "ProposedCategoryId",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropColumn(
                name: "ProposedFactor",
                table: "StockDocumentProvisionalItems");

            migrationBuilder.DropColumn(
                name: "ProposedProductVariantId",
                table: "StockDocumentProvisionalItems");
        }
    }
}

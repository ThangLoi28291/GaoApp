using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUnitConversionIdToOrderLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductUnitConversionId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_ProductUnitConversionId",
                table: "OrderLines",
                columns: new[] { "StoreId", "ProductUnitConversionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_ProductUnitConversionId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ProductUnitConversionId",
                table: "OrderLines");
        }
    }
}

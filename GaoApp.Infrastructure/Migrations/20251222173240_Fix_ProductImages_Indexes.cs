using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_ProductImages_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.RenameIndex(
            //    name: "UX_ProductImages_Primary_PerProduct",
            //    table: "ProductImages",
            //    newName: "IX_ProductImages_StoreId_ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.RenameIndex(
            //    name: "IX_ProductImages_StoreId_ProductId",
            //    table: "ProductImages",
            //    newName: "UX_ProductImages_Primary_PerProduct");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveInputInvoicePerReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active",
                table: "StockDocumentInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active",
                table: "StockDocumentInputInvoiceMap");
        }
    }
}

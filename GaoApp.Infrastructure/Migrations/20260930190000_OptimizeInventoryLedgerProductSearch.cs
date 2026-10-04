using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeInventoryLedgerProductSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LedgerProductTimeline",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "ProductVariantId", "OccurredAtUtc", "Id" },
                descending: new[] { false, false, true, true },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "WarehouseId", "TransactionType", "ReferenceType", "QuantityChange", "AfterQty" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_LedgerProductTimeline",
                table: "InventoryTransactions");
        }
    }
}

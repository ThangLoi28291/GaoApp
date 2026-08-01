using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPostingIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "IdempotencyKey",
                table: "InventoryTransactions",
                type: "varbinary(32)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryTransactions_StoreId_IdempotencyKey_Active",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_InventoryTransactions_StoreId_IdempotencyKey_Active",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "InventoryTransactions");
        }
    }
}

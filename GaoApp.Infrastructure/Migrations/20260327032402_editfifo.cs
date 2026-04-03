using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class editfifo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId1",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerId1",
                table: "InventoryValuationEntries");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryTransactionId",
                table: "OrderInventoryIssueLineAllocations",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "AllocatedQuantity",
                table: "OrderInventoryIssueLineAllocations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_InventoryCostLayerId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "InventoryCostLayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_EntryType_IsProvisional_CostFinalizedAtUtc_OccurredAtUtc_Id",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "EntryType", "IsProvisional", "CostFinalizedAtUtc", "OccurredAtUtc", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocations_InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerAllocationId",
                principalTable: "InventoryCostLayerAllocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayers_InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerId",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocations_InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayers_InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_StoreId_InventoryCostLayerId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_EntryType_IsProvisional_CostFinalizedAtUtc_OccurredAtUtc_Id",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryTransactionId",
                table: "OrderInventoryIssueLineAllocations",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AllocatedQuantity",
                table: "OrderInventoryIssueLineAllocations",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId1");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId1",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id");
        }
    }
}

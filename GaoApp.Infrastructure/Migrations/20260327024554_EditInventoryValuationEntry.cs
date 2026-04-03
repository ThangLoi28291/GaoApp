using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditInventoryValuationEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerId",
                table: "InventoryValuationEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryCostLayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    ReferenceSubKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsProvisionalSource = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostLayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_InventoryValuationEntries_InventoryValuationEntryId",
                        column: x => x.InventoryValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostLayerAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    ReverseOfAllocationId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByInventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCostLayerAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_InventoryCostLayerAllocations_ReverseOfAllocationId",
                        column: x => x.ReverseOfAllocationId,
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_InventoryCostLayers_InventoryCostLayerId",
                        column: x => x.InventoryCostLayerId,
                        principalTable: "InventoryCostLayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_InventoryCostLayers_ResolvedByInventoryCostLayerId",
                        column: x => x.ResolvedByInventoryCostLayerId,
                        principalTable: "InventoryCostLayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_InventoryValuationEntries_InventoryValuationEntryId",
                        column: x => x.InventoryValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId1");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_CostLayerId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_OpenProvisional",
                table: "InventoryCostLayerAllocations",
                columns: new[] { "StoreId", "IsProvisional", "IsResolved", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ResolvedByLayerId",
                table: "InventoryCostLayerAllocations",
                column: "ResolvedByInventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ReverseOfAllocationId",
                table: "InventoryCostLayerAllocations",
                column: "ReverseOfAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ValuationEntryId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_FIFO",
                table: "InventoryCostLayers",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_InventoryTransactionId",
                table: "InventoryCostLayers",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_InventoryValuationEntryId",
                table: "InventoryCostLayers",
                column: "InventoryValuationEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_Open",
                table: "InventoryCostLayers",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "RemainingQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_ProductVariantId",
                table: "InventoryCostLayers",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_WarehouseId",
                table: "InventoryCostLayers",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId1",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId1",
                table: "InventoryValuationEntries");

            migrationBuilder.DropTable(
                name: "InventoryCostLayerAllocations");

            migrationBuilder.DropTable(
                name: "InventoryCostLayers");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId1",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerId1",
                table: "InventoryValuationEntries");
        }
    }
}

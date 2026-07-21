using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase226HoldVoidRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderLegalEntityAllocationReversals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    OrderLegalEntityAllocationId = table.Column<int>(type: "int", nullable: false),
                    SalesReturnId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnLineId = table.Column<int>(type: "int", nullable: true),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    SourceValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    ReversalType = table.Column<byte>(type: "tinyint", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FinancialAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_OrderLegalEntityAllocationReversals", x => x.Id);
                    table.CheckConstraint("CK_OrderLegalEntityAllocationReversals_BaseQuantity_Positive", "[BaseQuantity] > 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocationReversals_FinancialAmount_NonNegative", "[FinancialAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_InventoryValuationEntries_SourceValuationEntryId",
                        column: x => x.SourceValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocations_OrderLegalEntityAllocationId",
                        column: x => x.OrderLegalEntityAllocationId,
                        principalTable: "OrderLegalEntityAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_SalesReturnLines_SalesReturnLineId",
                        column: x => x.SalesReturnLineId,
                        principalTable: "SalesReturnLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_Warehouses_StoreId_WarehouseId",
                        columns: x => new { x.StoreId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_InventoryTransactionId",
                table: "OrderLegalEntityAllocationReversals",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocationId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLegalEntityAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_ProductVariantId",
                table: "OrderLegalEntityAllocationReversals",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SalesReturnId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SalesReturnLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SourceValuationEntryId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SourceValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_LegalEntityId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_OrderLegalEntityAllocationId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_ReversalType_SalesReturnLineId_SourceValuationEntryId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "ReversalType", "SalesReturnLineId", "SourceValuationEntryId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_SalesReturnId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "SalesReturnId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_SourceValuationEntryId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "SourceValuationEntryId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_WarehouseId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "WarehouseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLegalEntityAllocationReversals");
        }
    }
}

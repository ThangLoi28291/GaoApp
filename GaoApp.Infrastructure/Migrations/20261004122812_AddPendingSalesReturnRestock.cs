using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingSalesReturnRestock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesReturnRestockFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnLineId = table.Column<int>(type: "int", nullable: false),
                    SourceValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    AllocationReversalId = table.Column<int>(type: "int", nullable: true),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_SalesReturnRestockFragments", x => x.Id);
                    table.CheckConstraint("CK_SalesReturnRestockFragments_Completion", "([InventoryTransactionId] IS NULL AND [CompletedAtUtc] IS NULL AND [CompletedByUserId] IS NULL) OR ([InventoryTransactionId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [CompletedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_SalesReturnRestockFragments_Quantity", "[BaseQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_SalesReturnRestockFragments_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnRestockFragments_InventoryValuationEntries_SourceValuationEntryId",
                        column: x => x.SourceValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnRestockFragments_OrderLegalEntityAllocationReversals_AllocationReversalId",
                        column: x => x.AllocationReversalId,
                        principalTable: "OrderLegalEntityAllocationReversals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnRestockFragments_SalesReturnLines_SalesReturnLineId",
                        column: x => x.SalesReturnLineId,
                        principalTable: "SalesReturnLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnRestockFragments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_AllocationReversalId",
                table: "SalesReturnRestockFragments",
                column: "AllocationReversalId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_InventoryTransactionId",
                table: "SalesReturnRestockFragments",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_SalesReturnLineId",
                table: "SalesReturnRestockFragments",
                column: "SalesReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_SourceValuationEntryId",
                table: "SalesReturnRestockFragments",
                column: "SourceValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_StoreId_CompletedAtUtc_IsDeleted",
                table: "SalesReturnRestockFragments",
                columns: new[] { "StoreId", "CompletedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_StoreId_SalesReturnLineId_SourceValuationEntryId",
                table: "SalesReturnRestockFragments",
                columns: new[] { "StoreId", "SalesReturnLineId", "SourceValuationEntryId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnRestockFragments_StoreId_SourceValuationEntryId_IsDeleted",
                table: "SalesReturnRestockFragments",
                columns: new[] { "StoreId", "SourceValuationEntryId", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesReturnRestockFragments");
        }
    }
}

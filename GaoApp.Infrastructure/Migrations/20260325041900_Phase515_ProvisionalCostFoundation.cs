using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase515_ProvisionalCostFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_OrderLines_OrderLineId",
                table: "SalesReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_SalesReturns_SalesReturnId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnLines_StoreId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreId_TransactionType_OccurredAtUtc",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc",
                table: "InventoryTransactions");

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisionalCost",
                table: "StockTransferLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCostTotal",
                table: "StockTransferLine",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "StockTransferLine",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisionalCost",
                table: "StockCountLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCostTotal",
                table: "StockCountLine",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "StockCountLine",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnQuantity",
                table: "SalesReturnLines",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnBaseQuantity",
                table: "SalesReturnLines",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundUnitAmount",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundLineTotal",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "SalesReturnLines",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisionalCost",
                table: "SalesReturnLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCostTotal",
                table: "SalesReturnLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "SalesReturnLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossProfit",
                table: "OrderLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisionalCost",
                table: "OrderLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCostTotal",
                table: "OrderLines",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "OrderLines",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceId",
                table: "InventoryTransactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChange",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "InventoryTransactions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BeforeQty",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AfterQty",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AddColumn<decimal>(
                name: "AfterInventoryValue",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BeforeInventoryValue",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostFinalizedAtUtc",
                table: "InventoryTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostSourceType",
                table: "InventoryTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisionalCost",
                table: "InventoryTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RunningAverageUnitCostAfter",
                table: "InventoryTransactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "InventoryTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "InventoryTransactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId1",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageUnitCost",
                table: "InventoryBalances",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryValue",
                table: "InventoryBalances",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInboundAtUtc",
                table: "InventoryBalances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastInboundUnitCost",
                table: "InventoryBalances",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastValuationAtUtc",
                table: "InventoryBalances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryValuationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningQtyAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningValueAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningAverageUnitCostAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    CostSourceType = table.Column<int>(type: "int", nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CostFinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevaluationOfEntryId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryValuationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryValuationEntries_RevaluationOfEntryId",
                        column: x => x.RevaluationOfEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_OrderLineId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_SalesReturnId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "SalesReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_VariantId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId_ReferenceLineId_TransactionType",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "TransactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId1",
                table: "InventoryTransactions",
                column: "WarehouseId1");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryTransactionId",
                table: "InventoryValuationEntries",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_ProductVariantId",
                table: "InventoryValuationEntries",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_RevaluationOfEntryId",
                table: "InventoryValuationEntries",
                column: "RevaluationOfEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_InventoryTransactionId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "InventoryTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_EntryType",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_RevaluationOfEntryId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "RevaluationOfEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_WarehouseId",
                table: "InventoryValuationEntries",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId1",
                table: "InventoryTransactions",
                column: "WarehouseId1",
                principalTable: "Warehouses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_OrderLines_OrderLineId",
                table: "SalesReturnLines",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_SalesReturns_SalesReturnId",
                table: "SalesReturnLines",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId1",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_OrderLines_OrderLineId",
                table: "SalesReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_SalesReturns_SalesReturnId",
                table: "SalesReturnLines");

            migrationBuilder.DropTable(
                name: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnLines_StoreId_OrderLineId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnLines_StoreId_SalesReturnId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnLines_StoreId_VariantId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId_ReferenceLineId_TransactionType",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_WarehouseId1",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "IsProvisionalCost",
                table: "StockTransferLine");

            migrationBuilder.DropColumn(
                name: "LineCostTotal",
                table: "StockTransferLine");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "StockTransferLine");

            migrationBuilder.DropColumn(
                name: "IsProvisionalCost",
                table: "StockCountLine");

            migrationBuilder.DropColumn(
                name: "LineCostTotal",
                table: "StockCountLine");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "StockCountLine");

            migrationBuilder.DropColumn(
                name: "IsProvisionalCost",
                table: "SalesReturnLines");

            migrationBuilder.DropColumn(
                name: "LineCostTotal",
                table: "SalesReturnLines");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "SalesReturnLines");

            migrationBuilder.DropColumn(
                name: "GrossProfit",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "IsProvisionalCost",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "LineCostTotal",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "AfterInventoryValue",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "BeforeInventoryValue",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "CostFinalizedAtUtc",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "CostSourceType",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "IsProvisionalCost",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "RunningAverageUnitCostAfter",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId1",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "AverageUnitCost",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "InventoryValue",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "LastInboundAtUtc",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "LastInboundUnitCost",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "LastValuationAtUtc",
                table: "InventoryBalances");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnQuantity",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnBaseQuantity",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundUnitAmount",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundLineTotal",
                table: "SalesReturnLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "SalesReturnLines",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceId",
                table: "InventoryTransactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChange",
                table: "InventoryTransactions",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "InventoryTransactions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BeforeQty",
                table: "InventoryTransactions",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "AfterQty",
                table: "InventoryTransactions",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId",
                table: "SalesReturnLines",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_TransactionType_OccurredAtUtc",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "TransactionType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_OrderLines_OrderLineId",
                table: "SalesReturnLines",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_SalesReturns_SalesReturnId",
                table: "SalesReturnLines",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

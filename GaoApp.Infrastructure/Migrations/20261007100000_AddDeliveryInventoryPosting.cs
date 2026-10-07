using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryInventoryPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM DeliveryJournalEntries WHERE [Kind]=1 OR [Kind]=2) OR EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments) OR EXISTS (SELECT 1 FROM InventoryReservations WHERE [ReferenceType]=11) THROW 51020, 'Existing delivery stock evidence cannot be fabricated or backfilled.', 1; IF EXISTS (SELECT 1 FROM InventoryBalances b OUTER APPLY (SELECT SUM(r.ReservedQty) Held FROM InventoryReservations r WHERE r.StoreId=b.StoreId AND r.WarehouseId=b.WarehouseId AND r.ProductVariantId=b.ProductVariantId AND r.Status=1 AND r.IsDeleted=0) s WHERE b.ReservedQty<>ISNULL(s.Held,0) OR b.ReservedQty<0) OR EXISTS (SELECT 1 FROM InventoryReservations r LEFT JOIN InventoryBalances b ON b.StoreId=r.StoreId AND b.WarehouseId=r.WarehouseId AND b.ProductVariantId=r.ProductVariantId WHERE r.Status=1 AND r.IsDeleted=0 AND (r.ReservedQty<=0 OR b.Id IS NULL)) THROW 51020, 'Existing reservation counters/source evidence require reconciliation.', 1;");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryJournalEntries");

            migrationBuilder.AddColumn<byte>(
                name: "PostingPrecisionPolicy",
                table: "InventoryTransactions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReservedQty",
                table: "InventoryReservations",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldDefaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryOrderId",
                table: "InventoryReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryOrderLineId",
                table: "InventoryReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryRevision",
                table: "InventoryReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedAtUtc",
                table: "InventoryReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedByUserId",
                table: "InventoryReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceLegalEntityId",
                table: "InventoryReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetAnchorAmount",
                table: "InventoryCostLayers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetAnchorQuantity",
                table: "InventoryCostLayers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "CostBudgetPolicy",
                table: "InventoryCostLayers",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingCostAmount",
                table: "InventoryCostLayers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "QuantityPrecisionVersion",
                table: "InventoryBalances",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryTransactionId",
                table: "DeliveryJournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "DeliveryJournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryJournalEntryId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostLayerId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryTransactionId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id_InventoryTransactionId_WarehouseId_ProductVariantId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "Id", "InventoryTransactionId", "WarehouseId", "ProductVariantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryTransactions_StoreId_Id",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryTransactions_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryCostLayers_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryCostLayers",
                columns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryCostLayerAllocations_StoreId_Id_InventoryValuationEntryId",
                table: "InventoryCostLayerAllocations",
                columns: new[] { "StoreId", "Id", "InventoryValuationEntryId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DeliveryOrderLines_StoreId_DeliveryOrderId_Id_VariantId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "Id", "VariantId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DeliveryJournalEntries_StoreId_DeliveryOrderId_Id_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId", "Id", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryOrderLineId_Id_SourceWarehouseId_SourceLegalEntityId_VariantId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "Id", "SourceWarehouseId", "SourceLegalEntityId", "VariantId" });

            migrationBuilder.CreateTable(
                name: "DeliveryReturnedCostFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    DeliveryJournalEntryId = table.Column<int>(type: "int", nullable: false),
                    DispatchCostFragmentId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    StartQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EndQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryReturnedCostFragments", x => x.Id);
                    table.CheckConstraint("CK_DeliveryReturnedCostFragments_Interval", "[IsDeleted]=0 AND [StartQuantity]>=0 AND [EndQuantity]>[StartQuantity] AND [BaseQuantity]=[EndQuantity]-[StartQuantity] AND [UnitCost]>=0 AND [CostAmount]>=0");
                    table.ForeignKey(
                        name: "FK_DeliveryReturnedCostFragments_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryOrderLineId_DispatchCostFragment~",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.DispatchCostFragmentId, x.SourceWarehouseId, x.SourceLegalEntityId, x.VariantId },
                        principalTable: "DeliveryDispatchCostFragments",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "Id", "SourceWarehouseId", "SourceLegalEntityId", "VariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryReturnedCostFragments_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_Source~",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryJournalEntryId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryJournalEntries",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryReturnedCostFragments_InventoryValuationEntries_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWareh~",
                        columns: x => new { x.StoreId, x.InventoryValuationEntryId, x.InventoryTransactionId, x.SourceWarehouseId, x.VariantId },
                        principalTable: "InventoryValuationEntries",
                        principalColumns: new[] { "StoreId", "Id", "InventoryTransactionId", "WarehouseId", "ProductVariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryReturnedCostFragments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostBudgetDebits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerAllocationId = table.Column<int>(type: "int", nullable: false),
                    AnchorQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AnchorAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StartQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EndQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsProvisionalResolution = table.Column<bool>(type: "bit", nullable: false),
                    ResolutionStartQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ResolutionEndQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RevaluationEntryId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_InventoryCostBudgetDebits", x => x.Id);
                    table.UniqueConstraint("AK_InventoryCostBudgetDebits_StoreId_Id_WarehouseId_ProductVariantId_SourceLegalEntityId_InventoryCostLayerId_InventoryTransact~", x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId, x.SourceLegalEntityId, x.InventoryCostLayerId, x.InventoryTransactionId, x.InventoryValuationEntryId, x.InventoryCostLayerAllocationId });
                    table.CheckConstraint("CK_InventoryCostBudgetDebits_Interval", "[IsDeleted]=0 AND [AnchorQuantity]>0 AND [AnchorAmount]>=0 AND [StartQuantity]>=0 AND [EndQuantity]>[StartQuantity] AND [EndQuantity]<=[AnchorQuantity] AND [UnitCost]>=0 AND [Amount]>=0 AND (([IsProvisionalResolution]=0 AND [ResolutionStartQuantity] IS NULL AND [ResolutionEndQuantity] IS NULL AND [RevaluationEntryId] IS NULL) OR ([IsProvisionalResolution]=1 AND [ResolutionStartQuantity] IS NOT NULL AND [ResolutionEndQuantity] IS NOT NULL AND [ResolutionStartQuantity]>=0 AND [ResolutionEndQuantity]>[ResolutionStartQuantity] AND [ResolutionEndQuantity]-[ResolutionStartQuantity]=[EndQuantity]-[StartQuantity]))");
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                        columns: x => new { x.StoreId, x.InventoryCostLayerAllocationId, x.InventoryValuationEntryId },
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumns: new[] { "StoreId", "Id", "InventoryValuationEntryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_InventoryCostLayers_StoreId_InventoryCostLayerId_WarehouseId_ProductVariantId",
                        columns: x => new { x.StoreId, x.InventoryCostLayerId, x.WarehouseId, x.ProductVariantId },
                        principalTable: "InventoryCostLayers",
                        principalColumns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_InventoryTransactions_StoreId_InventoryTransactionId_WarehouseId_ProductVariantId",
                        columns: x => new { x.StoreId, x.InventoryTransactionId, x.WarehouseId, x.ProductVariantId },
                        principalTable: "InventoryTransactions",
                        principalColumns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_InventoryValuationEntries_StoreId_InventoryValuationEntryId_WarehouseId_ProductVariantId",
                        columns: x => new { x.StoreId, x.InventoryValuationEntryId, x.WarehouseId, x.ProductVariantId },
                        principalTable: "InventoryValuationEntries",
                        principalColumns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_InventoryValuationEntries_StoreId_RevaluationEntryId",
                        columns: x => new { x.StoreId, x.RevaluationEntryId },
                        principalTable: "InventoryValuationEntries",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryCostBudgetDebits_Warehouses_StoreId_WarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.WarehouseId, x.SourceLegalEntityId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id", "LegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_DeliveryPosting",
                table: "InventoryTransactions",
                sql: "[PostingPrecisionPolicy]<>2 OR ([ReferenceType]=11 AND [ReferenceLineId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL AND (([TransactionType]=80 AND [QuantityChange]<0) OR ([TransactionType]=81 AND [QuantityChange]>0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_PostingPrecision",
                table: "InventoryTransactions",
                sql: "[PostingPrecisionPolicy]=0 OR [PostingPrecisionPolicy]=1 OR [PostingPrecisionPolicy]=2");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_DeliveryOrderId_DeliveryOrderLineId_ProductVariantId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_DeliveryOrderId_WarehouseId_SourceLegalEntityId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "DeliveryOrderId", "WarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_ReportedByUserId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "ReportedByUserId" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryReservations_ActiveDeliveryLine",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId" },
                unique: true,
                filter: "[ReferenceType]=11 AND [Status]=1 AND [IsDeleted]=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryReservations_DeliverySource",
                table: "InventoryReservations",
                sql: "([ReferenceType]<>11 AND [DeliveryOrderId] IS NULL AND [DeliveryOrderLineId] IS NULL AND [SourceLegalEntityId] IS NULL AND [DeliveryRevision] IS NULL AND [ReportedByUserId] IS NULL AND [ReportedAtUtc] IS NULL) OR ([ReferenceType]=11 AND [IsDeleted]=0 AND [DeliveryOrderId] IS NOT NULL AND [DeliveryOrderLineId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [DeliveryRevision] IS NOT NULL AND [DeliveryRevision]>0 AND [ReportedByUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND [ReservedQty]>0 AND [ReferenceLineId] IS NOT NULL AND [ReferenceLineId]=[DeliveryOrderLineId] AND [ReferenceId]=CONVERT(nvarchar(50),[DeliveryOrderId]) AND ([Status]=1 OR [Status]=2 OR [Status]=3))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostLayers_Budget",
                table: "InventoryCostLayers",
                sql: "([CostBudgetPolicy]=0 AND [BudgetAnchorQuantity] IS NULL AND [BudgetAnchorAmount] IS NULL AND [RemainingCostAmount] IS NULL) OR ([CostBudgetPolicy]=1 AND [BudgetAnchorQuantity] IS NOT NULL AND [BudgetAnchorQuantity]>0 AND [BudgetAnchorAmount] IS NOT NULL AND [BudgetAnchorAmount]>=0 AND [RemainingCostAmount] IS NOT NULL AND [RemainingCostAmount]>=0 AND [RemainingCostAmount]<=[BudgetAnchorAmount] AND [RemainingQuantity]>=0 AND [RemainingQuantity]<=[BudgetAnchorQuantity] AND [UnitCost]>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBalances_DeliveryPrecision",
                table: "InventoryBalances",
                sql: "[QuantityPrecisionVersion]=0 OR [QuantityPrecisionVersion]=1");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId" },
                unique: true,
                filter: "[Kind]=1");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_InventoryTransactionId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "InventoryTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_VariantId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryJournalEntries_StockSource",
                table: "DeliveryJournalEntries",
                sql: "([Kind]<>1 AND [Kind]<>2) OR ([DeliveryOrderLineId] IS NOT NULL AND [VariantId] IS NOT NULL AND [InventoryTransactionId] IS NOT NULL AND [BaseQuantity]>0 AND [MoneyAmount]=0 AND [SaleOrderId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryJournalEntryId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostBudgetDebitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId_SourceWarehouseId_VariantId_SourceLegalEntityId_InventoryCo~",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostBudgetDebitId", "SourceWarehouseId", "VariantId", "SourceLegalEntityId", "InventoryCostLayerId", "InventoryTransactionId", "InventoryValuationEntryId", "InventoryCostLayerAllocationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryDispatchCostFragments_Positive",
                table: "DeliveryDispatchCostFragments",
                sql: "[BaseQuantity]>0");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnedCostFragments_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryReturnedCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryJournalEntryId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnedCostFragments_StoreId_DeliveryOrderId_DeliveryOrderLineId_DispatchCostFragmentId_SourceWarehouseId_SourceLeg~",
                table: "DeliveryReturnedCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "DispatchCostFragmentId", "SourceWarehouseId", "SourceLegalEntityId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnedCostFragments_StoreId_DispatchCostFragmentId_StartQuantity",
                table: "DeliveryReturnedCostFragments",
                columns: new[] { "StoreId", "DispatchCostFragmentId", "StartQuantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnedCostFragments_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWarehouseId_VariantId",
                table: "DeliveryReturnedCostFragments",
                columns: new[] { "StoreId", "InventoryValuationEntryId", "InventoryTransactionId", "SourceWarehouseId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId", "InventoryValuationEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryCostLayerAllocationId_ResolutionStartQuantity",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId", "ResolutionStartQuantity" },
                unique: true,
                filter: "[IsProvisionalResolution]=1");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryCostLayerId_StartQuantity",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryCostLayerId", "StartQuantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryCostLayerId_WarehouseId_ProductVariantId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryCostLayerId", "WarehouseId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryTransactionId_WarehouseId_ProductVariantId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryTransactionId", "WarehouseId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_InventoryValuationEntryId_WarehouseId_ProductVariantId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "InventoryValuationEntryId", "WarehouseId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_RevaluationEntryId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "RevaluationEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostBudgetDebits_StoreId_WarehouseId_SourceLegalEntityId",
                table: "InventoryCostBudgetDebits",
                columns: new[] { "StoreId", "WarehouseId", "SourceLegalEntityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryDispatchCostFragments_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_Source~",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryJournalEntryId", "SourceWarehouseId", "SourceLegalEntityId" },
                principalTable: "DeliveryJournalEntries",
                principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostBudgetDebits_StoreId_InventoryCostBudgetDebitId_SourceWarehouseId_VariantId_Sourc~",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostBudgetDebitId", "SourceWarehouseId", "VariantId", "SourceLegalEntityId", "InventoryCostLayerId", "InventoryTransactionId", "InventoryValuationEntryId", "InventoryCostLayerAllocationId" },
                principalTable: "InventoryCostBudgetDebits",
                principalColumns: new[] { "StoreId", "Id", "WarehouseId", "ProductVariantId", "SourceLegalEntityId", "InventoryCostLayerId", "InventoryTransactionId", "InventoryValuationEntryId", "InventoryCostLayerAllocationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryJournalEntries_InventoryTransactions_StoreId_InventoryTransactionId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "InventoryTransactionId" },
                principalTable: "InventoryTransactions",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryJournalEntries_ProductVariant_StoreId_VariantId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "VariantId" },
                principalTable: "ProductVariant",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReservations_DeliveryOrderLines_StoreId_DeliveryOrderId_DeliveryOrderLineId_ProductVariantId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "ProductVariantId" },
                principalTable: "DeliveryOrderLines",
                principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id", "VariantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReservations_DeliveryOrders_StoreId_DeliveryOrderId_WarehouseId_SourceLegalEntityId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "DeliveryOrderId", "WarehouseId", "SourceLegalEntityId" },
                principalTable: "DeliveryOrders",
                principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReservations_UserInStores_StoreId_ReportedByUserId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "ReportedByUserId" },
                principalTable: "UserInStores",
                principalColumns: new[] { "StoreId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryBalances_DeliveryPrecision] ON [InventoryBalances] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.QuantityPrecisionVersion=1 AND (i.Id IS NULL OR i.QuantityPrecisionVersion<>1 OR i.IsDeleted=1 OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId)) THROW 51021, 'Promoted balance scope/precision cannot be downgraded or deleted.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryReservations_DeliverySource] ON [InventoryReservations] AFTER INSERT, UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.ReferenceType<>11 AND i.ReferenceType=11)\n THROW 51022, 'Historical reservations cannot be relabeled as delivery holds.', 1;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.ReferenceType=11 AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.ReferenceType<>d.ReferenceType OR i.ReferenceId<>d.ReferenceId OR i.ReferenceLineId<>d.ReferenceLineId OR i.DeliveryOrderId<>d.DeliveryOrderId OR i.DeliveryOrderLineId<>d.DeliveryOrderLineId OR i.SourceLegalEntityId<>d.SourceLegalEntityId OR i.DeliveryRevision<>d.DeliveryRevision OR i.ReportedByUserId<>d.ReportedByUserId OR i.ReportedAtUtc<>d.ReportedAtUtc OR i.ReservedQty<>d.ReservedQty OR i.ReservedAtUtc<>d.ReservedAtUtc OR i.IsDeleted<>0 OR d.Status<>1 AND i.Status<>d.Status OR d.Status=1 AND i.Status<>1 AND i.Status<>2 AND i.Status<>3))\n THROW 51022, 'Delivery hold identity/report quantity is immutable; released intervals cannot be restored.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id LEFT JOIN DeliveryOrderLines q ON q.StoreId=i.StoreId AND q.DeliveryOrderId=i.DeliveryOrderId AND q.Id=i.DeliveryOrderLineId AND q.VariantId=i.ProductVariantId LEFT JOIN DeliveryOrders o ON o.StoreId=i.StoreId AND o.Id=i.DeliveryOrderId AND o.SourceWarehouseId=i.WarehouseId AND o.SourceLegalEntityId=i.SourceLegalEntityId LEFT JOIN DeliveryPickingLines p ON p.StoreId=i.StoreId AND p.DeliveryOrderId=i.DeliveryOrderId AND p.DeliveryOrderLineId=i.DeliveryOrderLineId WHERE i.ReferenceType=11 AND (i.Status<>1 AND i.ReleasedAtUtc IS NULL OR d.Id IS NULL AND (i.Status<>1 OR o.Id IS NULL OR q.Id IS NULL OR p.Id IS NULL OR p.IsActive=0 OR p.ReportedQuantity IS NULL OR p.ReporterUserId IS NULL OR p.ReportedAtUtc IS NULL OR p.ReporterUserId<>i.ReportedByUserId OR p.ReportedAtUtc<>i.ReportedAtUtc OR i.DeliveryRevision>o.Revision+1 OR o.State<>1 AND o.State<>2 AND o.State<>3 OR o.State=3 AND p.ApprovedQuantity IS NULL OR i.ReservedQty<>CASE WHEN o.State=3 THEN p.ApprovedQuantity ELSE p.ReportedQuantity END*q.BaseMultiplier)))\n THROW 51022, 'Delivery hold must match the explicit current picking/source facts.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryTransactions_DeliveryProof] ON [InventoryTransactions] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.PostingPrecisionPolicy=0 AND i.PostingPrecisionPolicy<>0)\n THROW 51023, 'Historical transactions cannot be relabeled with a posting precision policy.', 1;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.PostingPrecisionPolicy<>0 AND (i.Id IS NULL OR i.PostingPrecisionPolicy<>d.PostingPrecisionPolicy OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.TransactionType<>d.TransactionType OR i.ReferenceType<>d.ReferenceType OR ISNULL(i.ReferenceId,'')<>ISNULL(d.ReferenceId,'') OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR ISNULL(i.ReferenceSubKey,'')<>ISNULL(d.ReferenceSubKey,'') OR ISNULL(i.IdempotencyKey,0x)<>ISNULL(d.IdempotencyKey,0x) OR i.QuantityChange<>d.QuantityChange OR i.BeforeQty<>d.BeforeQty OR i.AfterQty<>d.AfterQty OR i.IsDeleted=1 OR d.PostingPrecisionPolicy=2 AND d.CostFinalizedAtUtc IS NOT NULL AND (i.TotalCost<>d.TotalCost OR i.UnitCostSnapshot<>d.UnitCostSnapshot OR i.BeforeInventoryValue<>d.BeforeInventoryValue OR i.AfterInventoryValue<>d.AfterInventoryValue OR i.IsProvisionalCost<>d.IsProvisionalCost OR ISNULL(i.CostFinalizedAtUtc,'19000101')<>d.CostFinalizedAtUtc)))\n THROW 51023, 'Posted precision/source identity and finalized delivery stock values are immutable.', 1;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.CostFinalizedAtUtc IS NOT NULL AND (EXISTS (SELECT 1 FROM InventoryCostBudgetDebits b WHERE b.StoreId=d.StoreId AND b.InventoryTransactionId=d.Id) OR EXISTS (SELECT 1 FROM InventoryCostLayers l WHERE l.StoreId=d.StoreId AND l.InventoryTransactionId=d.Id AND l.CostBudgetPolicy=1) OR EXISTS (SELECT 1 FROM InventoryValuationEntries v WHERE v.StoreId=d.StoreId AND v.InventoryTransactionId=d.Id AND EXISTS (SELECT 1 FROM InventoryCostBudgetDebits b WHERE b.StoreId=v.StoreId AND (b.InventoryValuationEntryId=v.Id OR b.RevaluationEntryId=v.Id)))) AND (i.Id IS NULL OR i.PostingPrecisionPolicy<>d.PostingPrecisionPolicy OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.TransactionType<>d.TransactionType OR i.ReferenceType<>d.ReferenceType OR ISNULL(i.ReferenceId,'')<>ISNULL(d.ReferenceId,'') OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR ISNULL(i.ReferenceSubKey,'')<>ISNULL(d.ReferenceSubKey,'') OR ISNULL(i.IdempotencyKey,0x)<>ISNULL(d.IdempotencyKey,0x) OR i.QuantityChange<>d.QuantityChange OR i.BeforeQty<>d.BeforeQty OR i.AfterQty<>d.AfterQty OR i.IsDeleted=1 OR i.TotalCost<>d.TotalCost OR i.UnitCostSnapshot<>d.UnitCostSnapshot OR i.BeforeInventoryValue<>d.BeforeInventoryValue OR i.AfterInventoryValue<>d.AfterInventoryValue OR i.CostSourceType<>d.CostSourceType OR i.IsProvisionalCost<>d.IsProvisionalCost OR i.RunningAverageUnitCostAfter<>d.RunningAverageUnitCostAfter OR ISNULL(i.CostFinalizedAtUtc,'19000101')<>d.CostFinalizedAtUtc))\n THROW 51023, 'Finalized budget proof-owner transaction identity and value snapshots are immutable.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryCostLayers_BudgetSource] ON [InventoryCostLayers] AFTER INSERT, UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE d.CostBudgetPolicy=1 AND (i.Id IS NULL OR i.CostBudgetPolicy<>1 OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.InventoryTransactionId<>d.InventoryTransactionId OR i.InventoryValuationEntryId<>d.InventoryValuationEntryId OR i.ReferenceType<>d.ReferenceType OR i.ReferenceId<>d.ReferenceId OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR ISNULL(i.ReferenceSubKey,'')<>ISNULL(d.ReferenceSubKey,'') OR i.OriginalQuantity<>d.OriginalQuantity OR i.UnitCost<>d.UnitCost OR i.BudgetAnchorQuantity<>d.BudgetAnchorQuantity OR i.BudgetAnchorAmount<>d.BudgetAnchorAmount OR i.IsProvisionalSource<>d.IsProvisionalSource OR i.IsDeleted=1 OR i.RemainingQuantity>d.RemainingQuantity OR i.RemainingCostAmount>d.RemainingCostAmount))\n THROW 51024, 'FIFO source/anchors are immutable and debited intervals cannot be restored.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id LEFT JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId LEFT JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId WHERE i.CostBudgetPolicy=1 AND (v.Id IS NULL OR t.Id IS NULL OR v.InventoryTransactionId<>t.Id OR v.WarehouseId<>i.WarehouseId OR v.ProductVariantId<>i.ProductVariantId OR t.WarehouseId<>i.WarehouseId OR t.ProductVariantId<>i.ProductVariantId OR v.EntryType<>1 OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL OR v.IsDeleted=1 OR t.IsDeleted=1 OR i.IsProvisionalSource=1 OR v.Quantity<>i.OriginalQuantity OR v.UnitCost<>i.UnitCost OR v.Amount<0 OR i.RemainingCostAmount<>i.BudgetAnchorAmount-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(i.BudgetAnchorAmount)*10000)*CONVERT(decimal(38,0),(i.BudgetAnchorQuantity-i.RemainingQuantity)*10000))-((CONVERT(decimal(38,0),(i.BudgetAnchorAmount)*10000)*CONVERT(decimal(38,0),(i.BudgetAnchorQuantity-i.RemainingQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.BudgetAnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(i.BudgetAnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(i.BudgetAnchorAmount)*10000)*CONVERT(decimal(38,0),(i.BudgetAnchorQuantity-i.RemainingQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.BudgetAnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(i.BudgetAnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR d.Id IS NULL AND (i.BudgetAnchorQuantity<>i.OriginalQuantity OR i.BudgetAnchorAmount<>v.Amount) OR d.CostBudgetPolicy=0 AND i.BudgetAnchorQuantity<>d.RemainingQuantity))\n THROW 51024, 'FIFO budget must preserve a Final same-scope source and exact cumulative remainder.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId OUTER APPLY (SELECT SUM(CASE WHEN a.IsProvisional=1 THEN a.ResolvedAmount ELSE a.Amount END) UsedAmount,SUM(CASE WHEN a.IsProvisional=1 THEN a.ResolvedQuantity ELSE a.Quantity END) UsedQuantity FROM InventoryCostLayerAllocations a WHERE a.StoreId=i.StoreId AND (a.InventoryCostLayerId=i.Id OR a.ResolvedByInventoryCostLayerId=i.Id)) s WHERE d.CostBudgetPolicy=0 AND i.CostBudgetPolicy=1 AND (i.BudgetAnchorAmount<>v.Amount-ISNULL(s.UsedAmount,0) OR d.OriginalQuantity-d.RemainingQuantity<>ISNULL(s.UsedQuantity,0)))\n THROW 51024, 'Promoted budget anchors must preserve the existing source consumption history.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryValuationEntries_BudgetSource] ON [InventoryValuationEntries] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE (EXISTS (SELECT 1 FROM InventoryCostBudgetDebits b WHERE b.StoreId=d.StoreId AND (b.InventoryValuationEntryId=d.Id OR b.RevaluationEntryId=d.Id)) OR EXISTS (SELECT 1 FROM InventoryCostLayers l WHERE l.StoreId=d.StoreId AND l.InventoryValuationEntryId=d.Id AND l.CostBudgetPolicy=1)) AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.InventoryTransactionId<>d.InventoryTransactionId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.EntryType<>d.EntryType OR i.Quantity<>d.Quantity OR i.Amount<>d.Amount OR i.UnitCost<>d.UnitCost OR i.CostSourceType<>d.CostSourceType OR i.IsProvisional<>d.IsProvisional OR i.ReferenceType<>d.ReferenceType OR i.ReferenceId<>d.ReferenceId OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR ISNULL(i.ReferenceSubKey,'')<>ISNULL(d.ReferenceSubKey,'') OR CONVERT(varbinary(max),i.SourceReferenceSubKey)<>CONVERT(varbinary(max),d.SourceReferenceSubKey) OR DATALENGTH(i.SourceReferenceSubKey)<>DATALENGTH(d.SourceReferenceSubKey) OR i.SourceReferenceSubKey IS NULL AND d.SourceReferenceSubKey IS NOT NULL OR i.SourceReferenceSubKey IS NOT NULL AND d.SourceReferenceSubKey IS NULL OR d.CostFinalizedAtUtc IS NOT NULL AND (i.CostFinalizedAtUtc IS NULL OR i.CostFinalizedAtUtc<>d.CostFinalizedAtUtc) OR ISNULL(i.SourceValuationEntryId,0)<>ISNULL(d.SourceValuationEntryId,0) OR ISNULL(i.RevaluationOfEntryId,0)<>ISNULL(d.RevaluationOfEntryId,0) OR i.IsDeleted=1 OR d.InventoryCostLayerId IS NOT NULL AND ISNULL(i.InventoryCostLayerId,0)<>d.InventoryCostLayerId))\n THROW 51025, 'Budget valuation provenance/quantity/value cannot be edited or deleted.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryCostLayerAllocations_BudgetSource] ON [InventoryCostLayerAllocations] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE EXISTS (SELECT 1 FROM InventoryCostBudgetDebits b WHERE b.StoreId=d.StoreId AND b.InventoryCostLayerAllocationId=d.Id) AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.InventoryValuationEntryId<>d.InventoryValuationEntryId OR ISNULL(i.InventoryCostLayerId,0)<>ISNULL(d.InventoryCostLayerId,0) OR ISNULL(i.ReverseOfAllocationId,0)<>ISNULL(d.ReverseOfAllocationId,0) OR i.Quantity<>d.Quantity OR i.Amount<>d.Amount OR i.UnitCost<>d.UnitCost OR i.IsProvisional<>d.IsProvisional OR i.IsDeleted=1 OR i.ResolvedQuantity<d.ResolvedQuantity OR i.ResolvedQuantity>i.Quantity OR i.ResolvedAmount<d.ResolvedAmount OR d.IsProvisional=0 AND (i.ResolvedQuantity<>d.ResolvedQuantity OR i.ResolvedAmount<>d.ResolvedAmount OR i.IsResolved<>d.IsResolved OR ISNULL(i.ResolvedByInventoryCostLayerId,0)<>ISNULL(d.ResolvedByInventoryCostLayerId,0))))\n THROW 51025, 'Budget allocation source/value is immutable; resolution intervals cannot be restored.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryCostBudgetDebits_Immutable] ON [InventoryCostBudgetDebits] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51026, 'Cost debit intervals are append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryCostBudgetDebits_Source] ON [InventoryCostBudgetDebits] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN InventoryCostLayers l ON l.StoreId=i.StoreId AND l.Id=i.InventoryCostLayerId JOIN Warehouses w ON w.StoreId=i.StoreId AND w.Id=i.WarehouseId WHERE l.CostBudgetPolicy<>1 OR l.BudgetAnchorQuantity<>i.AnchorQuantity OR l.BudgetAnchorAmount<>i.AnchorAmount OR l.UnitCost<>i.UnitCost OR w.LegalEntityId<>i.SourceLegalEntityId OR i.EndQuantity>l.BudgetAnchorQuantity-l.RemainingQuantity OR i.Amount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))-((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))-((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(i.AnchorAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(i.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)))\n THROW 51026, 'Cost debit is not an exact already-consumed source interval.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN InventoryCostBudgetDebits b WITH (UPDLOCK,HOLDLOCK) ON b.StoreId=i.StoreId AND b.InventoryCostLayerId=i.InventoryCostLayerId AND b.Id<>i.Id AND b.StartQuantity<i.EndQuantity AND b.EndQuantity>i.StartQuantity)\n THROW 51026, 'Cost layer debit intervals cannot overlap.', 1;\n IF EXISTS (SELECT 1 FROM inserted i OUTER APPLY (SELECT MAX(b.EndQuantity) PreviousEnd FROM InventoryCostBudgetDebits b WITH (UPDLOCK,HOLDLOCK) WHERE b.StoreId=i.StoreId AND b.InventoryCostLayerId=i.InventoryCostLayerId AND b.Id<>i.Id AND b.EndQuantity<=i.StartQuantity) s WHERE i.StartQuantity<>ISNULL(s.PreviousEnd,0))\n THROW 51026, 'Cost layer debit proofs must extend the consumed prefix without holes.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId JOIN InventoryCostLayerAllocations a ON a.StoreId=i.StoreId AND a.Id=i.InventoryCostLayerAllocationId JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId WHERE v.IsDeleted=1 OR a.IsDeleted=1 OR t.IsDeleted=1 OR i.IsProvisionalResolution=0 AND (v.InventoryTransactionId<>t.Id OR v.EntryType<>2 OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL OR v.Quantity<>-(i.EndQuantity-i.StartQuantity) OR v.Amount<>-i.Amount OR v.UnitCost<>i.UnitCost OR a.InventoryCostLayerId IS NULL OR a.InventoryCostLayerId<>i.InventoryCostLayerId OR a.IsProvisional=1 OR a.Quantity<>i.EndQuantity-i.StartQuantity OR a.Amount<>i.Amount OR a.UnitCost<>i.UnitCost OR t.QuantityChange>=0) OR i.IsProvisionalResolution=1 AND (v.EntryType<>2 OR v.IsProvisional=0 OR v.Quantity<>-a.Quantity OR v.Amount<>-a.Amount OR a.IsProvisional=0 OR a.ResolvedQuantity<i.ResolutionEndQuantity OR a.ResolvedByInventoryCostLayerId IS NULL OR a.ResolvedByInventoryCostLayerId<>i.InventoryCostLayerId OR i.ResolutionEndQuantity>a.Quantity OR t.TransactionType<>70 OR t.QuantityChange<>0 OR t.TotalCost<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionEndQuantity)*10000))-((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionEndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionEndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionStartQuantity)*10000))-((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(a.Amount)*10000)*CONVERT(decimal(38,0),(i.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(a.Quantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-i.Amount OR i.RevaluationEntryId IS NULL AND t.TotalCost<>0))\n THROW 51026, 'Cost debit valuation/allocation/transaction subtype chain does not match.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN InventoryCostBudgetDebits b WITH (UPDLOCK,HOLDLOCK) ON b.StoreId=i.StoreId AND b.InventoryCostLayerAllocationId=i.InventoryCostLayerAllocationId AND b.Id<>i.Id AND b.IsProvisionalResolution=1 AND i.IsProvisionalResolution=1 AND b.ResolutionStartQuantity<i.ResolutionEndQuantity AND b.ResolutionEndQuantity>i.ResolutionStartQuantity)\n THROW 51026, 'Provisional resolution intervals cannot overlap.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN InventoryValuationEntries r ON r.StoreId=i.StoreId AND r.Id=i.RevaluationEntryId JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId WHERE i.RevaluationEntryId IS NOT NULL AND (r.Id IS NULL OR r.InventoryTransactionId<>t.Id OR r.WarehouseId<>i.WarehouseId OR r.ProductVariantId<>i.ProductVariantId OR r.EntryType<>3 OR r.Quantity<>0 OR r.Amount<>t.TotalCost OR ISNULL(r.SourceValuationEntryId,0)<>i.InventoryValuationEntryId OR ISNULL(r.RevaluationOfEntryId,0)<>i.InventoryValuationEntryId OR ISNULL(r.InventoryCostLayerId,0)<>i.InventoryCostLayerId OR r.IsDeleted=1 OR r.IsProvisional=1 OR r.CostFinalizedAtUtc IS NULL))\n THROW 51026, 'Resolution adjustment must mirror its neutral transaction and original source.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryJournalEntries_StockSource] ON [DeliveryJournalEntries] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId LEFT JOIN DeliveryOrderLines q ON q.StoreId=i.StoreId AND q.DeliveryOrderId=i.DeliveryOrderId AND q.Id=i.DeliveryOrderLineId WHERE (i.Kind=1 OR i.Kind=2) AND (t.Id IS NULL OR q.Id IS NULL OR q.VariantId<>i.VariantId OR t.WarehouseId<>i.SourceWarehouseId OR t.ProductVariantId<>i.VariantId OR t.PostingPrecisionPolicy<>2 OR t.ReferenceType<>11 OR t.ReferenceId<>CONVERT(nvarchar(64),i.DeliveryOrderId) OR t.ReferenceLineId<>i.DeliveryOrderLineId OR t.IsProvisionalCost=1 OR t.CostFinalizedAtUtc IS NULL OR t.IsDeleted=1 OR i.Kind=1 AND (t.TransactionType<>80 OR t.QuantityChange<>-i.BaseQuantity OR t.TotalCost<>-i.CostAmount) OR i.Kind=2 AND (t.TransactionType<>81 OR t.QuantityChange<>i.BaseQuantity OR t.TotalCost<>i.CostAmount)))\n THROW 51027, 'Stock journal must match its original delivery line and genuine Final movement.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryDispatchCostFragments_StockSource] ON [DeliveryDispatchCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryCostBudgetDebits b ON b.StoreId=i.StoreId AND b.Id=i.InventoryCostBudgetDebitId WHERE j.Kind<>1 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR b.IsProvisionalResolution=1 OR i.BaseQuantity<>b.EndQuantity-b.StartQuantity OR i.UnitCost<>b.UnitCost OR i.CostAmount<>b.Amount)\n THROW 51027, 'Dispatch fragment must use its exact ordinary stock debit/source journal.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId CROSS APPLY (SELECT SUM(f.BaseQuantity) Qty,SUM(f.CostAmount) Amount FROM DeliveryDispatchCostFragments f WITH (UPDLOCK,HOLDLOCK) WHERE f.StoreId=i.StoreId AND f.DeliveryJournalEntryId=j.Id) s WHERE s.Qty>j.BaseQuantity OR s.Amount>j.CostAmount)\n THROW 51027, 'Dispatch fragment quantity/value exceeds journal budget.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryReturnedCostFragments_Immutable] ON [DeliveryReturnedCostFragments] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51028, 'Accepted stock-return intervals are append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryReturnedCostFragments_StockSource] ON [DeliveryReturnedCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId JOIN InventoryCostBudgetDebits b ON b.StoreId=f.StoreId AND b.Id=f.InventoryCostBudgetDebitId JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId WHERE i.EndQuantity>f.BaseQuantity OR i.UnitCost<>f.UnitCost OR i.CostAmount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR j.Kind<>2 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR j.BaseQuantity<>i.BaseQuantity OR j.CostAmount<>i.CostAmount OR v.EntryType<>1 OR v.SourceValuationEntryId IS NULL OR v.SourceValuationEntryId<>f.InventoryValuationEntryId OR v.Quantity<>i.BaseQuantity OR v.UnitCost<>i.UnitCost OR v.Amount<>i.CostAmount OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL OR v.IsDeleted=1)\n THROW 51028, 'Accepted return must preserve its exact dispatch-tail cost and genuine inbound source.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.DispatchCostFragmentId=i.DispatchCostFragmentId AND r.Id<>i.Id AND r.StartQuantity<i.EndQuantity AND r.EndQuantity>i.StartQuantity)\n THROW 51028, 'Accepted return intervals cannot overlap.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId CROSS APPLY (SELECT MIN(r.StartQuantity) Start,SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=i.StoreId AND r.DispatchCostFragmentId=f.Id) s WHERE s.Start+s.Qty<>f.BaseQuantity)\n THROW 51028, 'Accepted returns must extend the tail toward the prefix without holes.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments later ON later.StoreId=i.StoreId AND later.DeliveryOrderId=i.DeliveryOrderId AND later.DeliveryOrderLineId=i.DeliveryOrderLineId AND later.Id>i.DispatchCostFragmentId OUTER APPLY (SELECT SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=later.StoreId AND r.DispatchCostFragmentId=later.Id) s WHERE ISNULL(s.Qty,0)<>later.BaseQuantity)\n THROW 51028, 'Receive the latest dispatch fragment tail before an earlier source fragment.', 1;\nEND;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM InventoryBalances WHERE QuantityPrecisionVersion=1) OR EXISTS (SELECT 1 FROM InventoryCostLayers WHERE CostBudgetPolicy=1) OR EXISTS (SELECT 1 FROM InventoryCostBudgetDebits) OR EXISTS (SELECT 1 FROM DeliveryReturnedCostFragments) OR EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries WHERE Kind=1 OR Kind=2) OR EXISTS (SELECT 1 FROM InventoryReservations WHERE ReferenceType=11 OR ReservedQty<>ROUND(ReservedQty,3)) OR EXISTS (SELECT 1 FROM InventoryTransactions WHERE PostingPrecisionPolicy<>0) THROW 51029, 'Delivery inventory proof or precision is occupied; use a forward fix.', 1;");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryReturnedCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryReturnedCostFragments_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryDispatchCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryJournalEntries_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryCostBudgetDebits_Source];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryCostBudgetDebits_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryCostLayerAllocations_BudgetSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryValuationEntries_BudgetSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryCostLayers_BudgetSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryTransactions_DeliveryProof];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryReservations_DeliverySource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryBalances_DeliveryPrecision];");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryDispatchCostFragments_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_Source~",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostBudgetDebits_StoreId_InventoryCostBudgetDebitId_SourceWarehouseId_VariantId_Sourc~",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryJournalEntries_InventoryTransactions_StoreId_InventoryTransactionId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryJournalEntries_ProductVariant_StoreId_VariantId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReservations_DeliveryOrderLines_StoreId_DeliveryOrderId_DeliveryOrderLineId_ProductVariantId",
                table: "InventoryReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReservations_DeliveryOrders_StoreId_DeliveryOrderId_WarehouseId_SourceLegalEntityId",
                table: "InventoryReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReservations_UserInStores_StoreId_ReportedByUserId",
                table: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "DeliveryReturnedCostFragments");

            migrationBuilder.DropTable(
                name: "InventoryCostBudgetDebits");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id",
                table: "InventoryValuationEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id_InventoryTransactionId_WarehouseId_ProductVariantId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryValuationEntries_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryTransactions_StoreId_Id",
                table: "InventoryTransactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryTransactions_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_DeliveryPosting",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_PostingPrecision",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReservations_StoreId_DeliveryOrderId_DeliveryOrderLineId_ProductVariantId",
                table: "InventoryReservations");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReservations_StoreId_DeliveryOrderId_WarehouseId_SourceLegalEntityId",
                table: "InventoryReservations");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReservations_StoreId_ReportedByUserId",
                table: "InventoryReservations");

            migrationBuilder.DropIndex(
                name: "UX_InventoryReservations_ActiveDeliveryLine",
                table: "InventoryReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryReservations_DeliverySource",
                table: "InventoryReservations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryCostLayers_StoreId_Id_WarehouseId_ProductVariantId",
                table: "InventoryCostLayers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostLayers_Budget",
                table: "InventoryCostLayers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryCostLayerAllocations_StoreId_Id_InventoryValuationEntryId",
                table: "InventoryCostLayerAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryBalances_DeliveryPrecision",
                table: "InventoryBalances");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DeliveryOrderLines_StoreId_DeliveryOrderId_Id_VariantId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DeliveryJournalEntries_StoreId_DeliveryOrderId_Id_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryJournalEntries_StoreId_InventoryTransactionId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryJournalEntries_StoreId_VariantId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryJournalEntries_StockSource",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryOrderLineId_Id_SourceWarehouseId_SourceLegalEntityId_VariantId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryJournalEntryId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId_SourceWarehouseId_VariantId_SourceLegalEntityId_InventoryCo~",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryDispatchCostFragments_Positive",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "PostingPrecisionPolicy",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "DeliveryOrderId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "DeliveryOrderLineId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "DeliveryRevision",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "ReportedAtUtc",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "ReportedByUserId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "SourceLegalEntityId",
                table: "InventoryReservations");

            migrationBuilder.DropColumn(
                name: "BudgetAnchorAmount",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "BudgetAnchorQuantity",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "CostBudgetPolicy",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "RemainingCostAmount",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "QuantityPrecisionVersion",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "InventoryTransactionId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "DeliveryJournalEntries");

            migrationBuilder.DropColumn(
                name: "DeliveryJournalEntryId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "InventoryCostLayerId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "InventoryTransactionId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReservedQty",
                table: "InventoryReservations",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldDefaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId" });
        }
    }
}

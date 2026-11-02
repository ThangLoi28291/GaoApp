using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryProvisionalCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryReturnedCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryDispatchCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryJournalEntries_StockSource];");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisional",
                table: "DeliveryReturnedCostFragments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "InventoryCostLayerId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisional",
                table: "DeliveryDispatchCostFragments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostBudgetDebitId" },
                unique: true,
                filter: "[InventoryCostBudgetDebitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId", "InventoryValuationEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWarehouseId_VariantId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryValuationEntryId", "InventoryTransactionId", "SourceWarehouseId", "VariantId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryDispatchCostFragments_CostProof",
                table: "DeliveryDispatchCostFragments",
                sql: "([IsProvisional]=0 AND [InventoryCostLayerId] IS NOT NULL AND [InventoryCostBudgetDebitId] IS NOT NULL) OR ([IsProvisional]=1 AND [InventoryCostLayerId] IS NULL AND [InventoryCostBudgetDebitId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId", "InventoryValuationEntryId" },
                principalTable: "InventoryCostLayerAllocations",
                principalColumns: new[] { "StoreId", "Id", "InventoryValuationEntryId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryValuationEntries_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWareh~",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryValuationEntryId", "InventoryTransactionId", "SourceWarehouseId", "VariantId" },
                principalTable: "InventoryValuationEntries",
                principalColumns: new[] { "StoreId", "Id", "InventoryTransactionId", "WarehouseId", "ProductVariantId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryJournalEntries_StockSource] ON [DeliveryJournalEntries] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId LEFT JOIN DeliveryOrderLines q ON q.StoreId=i.StoreId AND q.DeliveryOrderId=i.DeliveryOrderId AND q.Id=i.DeliveryOrderLineId WHERE (i.Kind=1 OR i.Kind=2) AND (t.Id IS NULL OR q.Id IS NULL OR q.VariantId<>i.VariantId OR t.WarehouseId<>i.SourceWarehouseId OR t.ProductVariantId<>i.VariantId OR t.PostingPrecisionPolicy<>2 OR t.ReferenceType<>11 OR t.ReferenceId<>CONVERT(nvarchar(64),i.DeliveryOrderId) OR t.ReferenceLineId<>i.DeliveryOrderLineId OR t.IsDeleted=1 OR t.IsProvisionalCost=0 AND t.CostFinalizedAtUtc IS NULL OR t.IsProvisionalCost=1 AND (t.CostFinalizedAtUtc IS NOT NULL OR NOT EXISTS (SELECT 1 FROM InventoryValuationEntries v WHERE v.StoreId=t.StoreId AND v.InventoryTransactionId=t.Id AND v.WarehouseId=t.WarehouseId AND v.ProductVariantId=t.ProductVariantId AND v.IsDeleted=0 AND v.IsProvisional=1 AND v.CostFinalizedAtUtc IS NULL AND v.InventoryCostLayerId IS NULL AND ((i.Kind=1 AND v.EntryType=2 AND EXISTS (SELECT 1 FROM InventoryCostLayerAllocations a WHERE a.StoreId=v.StoreId AND a.InventoryValuationEntryId=v.Id AND a.IsProvisional=1 AND a.InventoryCostLayerId IS NULL AND a.ReverseOfAllocationId IS NULL AND a.Quantity=-v.Quantity AND a.Amount=-v.Amount AND a.UnitCost=v.UnitCost AND a.IsDeleted=0)) OR (i.Kind=2 AND v.EntryType=1 AND EXISTS (SELECT 1 FROM InventoryCostLayerAllocations a JOIN DeliveryDispatchCostFragments f ON f.StoreId=a.StoreId AND f.InventoryCostLayerAllocationId=a.ReverseOfAllocationId AND f.IsProvisional=1 WHERE a.StoreId=v.StoreId AND a.InventoryValuationEntryId=v.Id AND a.IsProvisional=1 AND a.InventoryCostLayerId IS NULL AND a.Quantity=v.Quantity AND a.Amount=v.Amount AND a.UnitCost=v.UnitCost AND a.IsDeleted=0 AND f.DeliveryOrderId=i.DeliveryOrderId AND f.DeliveryOrderLineId=i.DeliveryOrderLineId AND f.SourceWarehouseId=i.SourceWarehouseId AND f.SourceLegalEntityId=i.SourceLegalEntityId AND f.VariantId=i.VariantId))))) OR i.Kind=1 AND (t.TransactionType<>80 OR t.QuantityChange<>-i.BaseQuantity OR t.TotalCost<>-i.CostAmount) OR i.Kind=2 AND (t.TransactionType<>81 OR t.QuantityChange<>i.BaseQuantity OR t.TotalCost<>i.CostAmount)))\n THROW 51027, 'Stock journal must match its exact Final or genuinely provisional delivery movement.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryDispatchCostFragments_StockSource] ON [DeliveryDispatchCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId JOIN InventoryCostLayerAllocations a ON a.StoreId=i.StoreId AND a.Id=i.InventoryCostLayerAllocationId LEFT JOIN InventoryCostBudgetDebits b ON b.StoreId=i.StoreId AND b.Id=i.InventoryCostBudgetDebitId JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId WHERE j.Kind<>1 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR v.InventoryTransactionId<>t.Id OR v.ReferenceType<>11 OR v.ReferenceId<>CONVERT(nvarchar(64),i.DeliveryOrderId) OR v.ReferenceLineId IS NULL OR v.ReferenceLineId<>i.DeliveryOrderLineId OR v.EntryType<>2 OR v.Quantity<>-i.BaseQuantity OR v.Amount<>-i.CostAmount OR v.UnitCost<>i.UnitCost OR v.IsDeleted=1 OR a.InventoryValuationEntryId<>v.Id OR a.ReverseOfAllocationId IS NOT NULL OR a.Quantity<>i.BaseQuantity OR a.Amount<>i.CostAmount OR a.UnitCost<>i.UnitCost OR a.IsDeleted=1 OR i.IsProvisional=0 AND (b.Id IS NULL OR b.IsProvisionalResolution=1 OR i.BaseQuantity<>b.EndQuantity-b.StartQuantity OR i.UnitCost<>b.UnitCost OR i.CostAmount<>b.Amount OR a.IsProvisional=1 OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL) OR i.IsProvisional=1 AND (i.InventoryCostBudgetDebitId IS NOT NULL OR i.InventoryCostLayerId IS NOT NULL OR a.InventoryCostLayerId IS NOT NULL OR a.IsProvisional=0 OR a.ResolvedQuantity<>0 OR a.ResolvedAmount<>0 OR v.InventoryCostLayerId IS NOT NULL OR v.IsProvisional=0 OR v.CostFinalizedAtUtc IS NOT NULL OR t.IsProvisionalCost=0 OR t.CostFinalizedAtUtc IS NOT NULL))\n THROW 51027, 'Dispatch fragment must preserve its exact Final debit or original provisional allocation.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId CROSS APPLY (SELECT SUM(f.BaseQuantity) Qty,SUM(f.CostAmount) Amount FROM DeliveryDispatchCostFragments f WITH (UPDLOCK,HOLDLOCK) WHERE f.StoreId=i.StoreId AND f.DeliveryJournalEntryId=j.Id) s WHERE s.Qty>j.BaseQuantity OR s.Amount>j.CostAmount)\n THROW 51027, 'Dispatch fragment quantity/value exceeds journal budget.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryReturnedCostFragments_StockSource] ON [DeliveryReturnedCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId JOIN InventoryValuationEntries original ON original.StoreId=f.StoreId AND original.Id=f.InventoryValuationEntryId LEFT JOIN InventoryCostBudgetDebits b ON b.StoreId=f.StoreId AND b.Id=f.InventoryCostBudgetDebitId\n OUTER APPLY (SELECT TOP(1) d.* FROM InventoryCostBudgetDebits d WHERE d.StoreId=f.StoreId AND d.InventoryCostLayerAllocationId=f.InventoryCostLayerAllocationId AND d.IsProvisionalResolution=1 AND d.ResolutionStartQuantity<=i.StartQuantity AND d.ResolutionEndQuantity>=i.EndQuantity ORDER BY d.ResolutionStartQuantity) resolved\n OUTER APPLY (SELECT MAX(d.ResolutionEndQuantity) ResolvedEnd FROM InventoryCostBudgetDebits d WHERE d.StoreId=f.StoreId AND d.InventoryCostLayerAllocationId=f.InventoryCostLayerAllocationId AND d.IsProvisionalResolution=1) prefix\n WHERE i.EndQuantity>f.BaseQuantity OR j.Kind<>2 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR j.BaseQuantity<>i.BaseQuantity OR j.CostAmount<>i.CostAmount OR v.ReferenceType<>11 OR v.ReferenceId<>CONVERT(nvarchar(64),i.DeliveryOrderId) OR v.ReferenceLineId IS NULL OR v.ReferenceLineId<>i.DeliveryOrderLineId OR v.EntryType<>1 OR v.SourceValuationEntryId IS NULL OR v.SourceValuationEntryId<>f.InventoryValuationEntryId OR v.Quantity<>i.BaseQuantity OR v.UnitCost<>i.UnitCost OR v.Amount<>i.CostAmount OR v.IsDeleted=1 OR (v.SourceReferenceSubKey IS NULL AND original.ReferenceSubKey IS NOT NULL OR v.SourceReferenceSubKey IS NOT NULL AND original.ReferenceSubKey IS NULL OR DATALENGTH(v.SourceReferenceSubKey)<>DATALENGTH(original.ReferenceSubKey) OR CONVERT(varbinary(max),v.SourceReferenceSubKey)<>CONVERT(varbinary(max),original.ReferenceSubKey))\n OR f.IsProvisional=0 AND (i.IsProvisional=1 OR b.Id IS NULL OR i.UnitCost<>f.UnitCost OR i.CostAmount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL)\n OR f.IsProvisional=1 AND i.IsProvisional=1 AND (i.StartQuantity<ISNULL(prefix.ResolvedEnd,0) OR i.UnitCost<>f.UnitCost OR i.CostAmount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))-((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))-((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(f.CostAmount)*10000)*CONVERT(decimal(38,0),(i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(f.BaseQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR v.IsProvisional=0 OR v.CostFinalizedAtUtc IS NOT NULL OR v.InventoryCostLayerId IS NOT NULL OR (SELECT COUNT(*) FROM InventoryCostLayerAllocations a WHERE a.StoreId=i.StoreId AND a.InventoryValuationEntryId=v.Id AND a.ReverseOfAllocationId=f.InventoryCostLayerAllocationId AND a.InventoryCostLayerId IS NULL AND a.IsProvisional=1 AND a.IsResolved=1 AND a.Quantity=i.BaseQuantity AND a.Amount=i.CostAmount AND a.UnitCost=i.UnitCost AND a.IsDeleted=0)<>1)\n OR f.IsProvisional=1 AND i.IsProvisional=0 AND (resolved.Id IS NULL OR i.UnitCost<>resolved.UnitCost OR i.CostAmount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.EndQuantity-resolved.ResolutionStartQuantity)*10000))-((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.EndQuantity-resolved.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.EndQuantity-resolved.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.StartQuantity-resolved.ResolutionStartQuantity)*10000))-((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.StartQuantity-resolved.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(resolved.AnchorAmount)*10000)*CONVERT(decimal(38,0),(resolved.StartQuantity+i.StartQuantity-resolved.ResolutionStartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(resolved.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL OR NOT EXISTS (SELECT 1 FROM InventoryCostLayers l WHERE l.StoreId=i.StoreId AND l.InventoryValuationEntryId=v.Id AND l.InventoryTransactionId=i.InventoryTransactionId AND l.WarehouseId=i.SourceWarehouseId AND l.ProductVariantId=i.VariantId AND l.IsProvisionalSource=0 AND l.CostBudgetPolicy=1 AND l.OriginalQuantity=i.BaseQuantity AND l.BudgetAnchorQuantity=i.BaseQuantity AND l.BudgetAnchorAmount=i.CostAmount AND l.UnitCost=i.UnitCost AND l.IsDeleted=0)))\n THROW 51028, 'Accepted return must preserve its genuine Final, resolved, or unresolved dispatch-tail source.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.DispatchCostFragmentId=i.DispatchCostFragmentId AND r.Id<>i.Id AND r.StartQuantity<i.EndQuantity AND r.EndQuantity>i.StartQuantity)\n THROW 51028, 'Accepted return intervals cannot overlap.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId CROSS APPLY (SELECT MIN(r.StartQuantity) Start,SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=i.StoreId AND r.DispatchCostFragmentId=f.Id) s WHERE s.Start+s.Qty<>f.BaseQuantity)\n THROW 51028, 'Accepted returns must extend the tail toward the prefix without holes.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments later ON later.StoreId=i.StoreId AND later.DeliveryOrderId=i.DeliveryOrderId AND later.DeliveryOrderLineId=i.DeliveryOrderLineId AND later.Id>i.DispatchCostFragmentId OUTER APPLY (SELECT SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=later.StoreId AND r.DispatchCostFragmentId=later.Id) s WHERE ISNULL(s.Qty,0)<>later.BaseQuantity)\n THROW 51028, 'Receive the latest dispatch fragment tail before an earlier source fragment.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryTransactions_DeliveryProvisional] ON [InventoryTransactions] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE (EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments f WHERE f.StoreId=d.StoreId AND f.InventoryTransactionId=d.Id AND f.IsProvisional=1) OR EXISTS (SELECT 1 FROM DeliveryReturnedCostFragments r WHERE r.StoreId=d.StoreId AND r.InventoryTransactionId=d.Id AND r.IsProvisional=1)) AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.PostingPrecisionPolicy<>d.PostingPrecisionPolicy OR i.TransactionType<>d.TransactionType OR i.ReferenceType<>d.ReferenceType OR (i.ReferenceId IS NULL AND d.ReferenceId IS NOT NULL OR i.ReferenceId IS NOT NULL AND d.ReferenceId IS NULL OR DATALENGTH(i.ReferenceId)<>DATALENGTH(d.ReferenceId) OR CONVERT(varbinary(max),i.ReferenceId)<>CONVERT(varbinary(max),d.ReferenceId)) OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR (i.ReferenceSubKey IS NULL AND d.ReferenceSubKey IS NOT NULL OR i.ReferenceSubKey IS NOT NULL AND d.ReferenceSubKey IS NULL OR DATALENGTH(i.ReferenceSubKey)<>DATALENGTH(d.ReferenceSubKey) OR CONVERT(varbinary(max),i.ReferenceSubKey)<>CONVERT(varbinary(max),d.ReferenceSubKey)) OR ISNULL(i.IdempotencyKey,0x)<>ISNULL(d.IdempotencyKey,0x) OR i.QuantityChange<>d.QuantityChange OR i.BeforeQty<>d.BeforeQty OR i.AfterQty<>d.AfterQty OR i.TotalCost<>d.TotalCost OR i.UnitCostSnapshot<>d.UnitCostSnapshot OR i.BeforeInventoryValue<>d.BeforeInventoryValue OR i.AfterInventoryValue<>d.AfterInventoryValue OR i.RunningAverageUnitCostAfter<>d.RunningAverageUnitCostAfter OR i.CostSourceType<>d.CostSourceType OR i.IsProvisionalCost<>d.IsProvisionalCost OR i.CostFinalizedAtUtc IS NOT NULL OR i.OccurredAtUtc<>d.OccurredAtUtc OR i.IsDeleted=1))\n THROW 51041, 'Original provisional delivery transaction estimates and scope are immutable.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryValuationEntries_DeliveryProvisional] ON [InventoryValuationEntries] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE (EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments f WHERE f.StoreId=d.StoreId AND f.InventoryValuationEntryId=d.Id AND f.IsProvisional=1) OR EXISTS (SELECT 1 FROM DeliveryReturnedCostFragments r WHERE r.StoreId=d.StoreId AND r.InventoryValuationEntryId=d.Id AND r.IsProvisional=1)) AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.InventoryTransactionId<>d.InventoryTransactionId OR i.WarehouseId<>d.WarehouseId OR i.ProductVariantId<>d.ProductVariantId OR i.EntryType<>d.EntryType OR i.Quantity<>d.Quantity OR i.Amount<>d.Amount OR i.UnitCost<>d.UnitCost OR i.RunningQtyAfter<>d.RunningQtyAfter OR i.RunningValueAfter<>d.RunningValueAfter OR i.RunningAverageUnitCostAfter<>d.RunningAverageUnitCostAfter OR i.CostSourceType<>d.CostSourceType OR i.IsProvisional<>d.IsProvisional OR i.ReferenceType<>d.ReferenceType OR (i.ReferenceId IS NULL AND d.ReferenceId IS NOT NULL OR i.ReferenceId IS NOT NULL AND d.ReferenceId IS NULL OR DATALENGTH(i.ReferenceId)<>DATALENGTH(d.ReferenceId) OR CONVERT(varbinary(max),i.ReferenceId)<>CONVERT(varbinary(max),d.ReferenceId)) OR ISNULL(i.ReferenceLineId,0)<>ISNULL(d.ReferenceLineId,0) OR (i.ReferenceSubKey IS NULL AND d.ReferenceSubKey IS NOT NULL OR i.ReferenceSubKey IS NOT NULL AND d.ReferenceSubKey IS NULL OR DATALENGTH(i.ReferenceSubKey)<>DATALENGTH(d.ReferenceSubKey) OR CONVERT(varbinary(max),i.ReferenceSubKey)<>CONVERT(varbinary(max),d.ReferenceSubKey)) OR (i.SourceReferenceSubKey IS NULL AND d.SourceReferenceSubKey IS NOT NULL OR i.SourceReferenceSubKey IS NOT NULL AND d.SourceReferenceSubKey IS NULL OR DATALENGTH(i.SourceReferenceSubKey)<>DATALENGTH(d.SourceReferenceSubKey) OR CONVERT(varbinary(max),i.SourceReferenceSubKey)<>CONVERT(varbinary(max),d.SourceReferenceSubKey)) OR ISNULL(i.SourceValuationEntryId,0)<>ISNULL(d.SourceValuationEntryId,0) OR ISNULL(i.RevaluationOfEntryId,0)<>ISNULL(d.RevaluationOfEntryId,0) OR ISNULL(i.InventoryCostLayerId,0)<>ISNULL(d.InventoryCostLayerId,0) OR i.IsDeleted=1 OR d.CostFinalizedAtUtc IS NOT NULL AND (i.CostFinalizedAtUtc IS NULL OR i.CostFinalizedAtUtc<>d.CostFinalizedAtUtc) OR d.CostFinalizedAtUtc IS NULL AND i.CostFinalizedAtUtc IS NOT NULL AND NOT EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments f JOIN InventoryCostLayerAllocations a ON a.StoreId=f.StoreId AND a.Id=f.InventoryCostLayerAllocationId WHERE f.StoreId=d.StoreId AND f.InventoryValuationEntryId=d.Id AND f.IsProvisional=1 AND a.ResolvedQuantity=a.Quantity)))\n THROW 51041, 'Original provisional valuation estimate/provenance is immutable; only genuine full resolution may finalize it.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_InventoryCostLayerAllocations_DeliveryProvisional] ON [InventoryCostLayerAllocations] AFTER UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id JOIN InventoryValuationEntries v ON v.StoreId=d.StoreId AND v.Id=d.InventoryValuationEntryId WHERE (EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments f WHERE f.StoreId=d.StoreId AND f.InventoryCostLayerAllocationId=d.Id AND f.IsProvisional=1) OR EXISTS (SELECT 1 FROM DeliveryReturnedCostFragments r WHERE r.StoreId=d.StoreId AND r.InventoryValuationEntryId=d.InventoryValuationEntryId AND r.IsProvisional=1)) AND (i.Id IS NULL OR i.StoreId<>d.StoreId OR i.InventoryValuationEntryId<>d.InventoryValuationEntryId OR ISNULL(i.ReverseOfAllocationId,0)<>ISNULL(d.ReverseOfAllocationId,0) OR i.Quantity<>d.Quantity OR i.Amount<>d.Amount OR i.UnitCost<>d.UnitCost OR i.IsProvisional<>d.IsProvisional OR i.IsDeleted=1 OR i.ResolvedQuantity<d.ResolvedQuantity OR i.ResolvedAmount<d.ResolvedAmount OR i.ResolvedQuantity>i.Quantity-(SELECT ISNULL(SUM(a.Quantity),0) FROM InventoryCostLayerAllocations a WHERE a.StoreId=d.StoreId AND a.ReverseOfAllocationId=d.Id AND a.IsProvisional=1 AND a.IsDeleted=0) OR i.ResolvedQuantity=d.ResolvedQuantity AND (i.ResolvedAmount<>d.ResolvedAmount OR ISNULL(i.ResolvedByInventoryCostLayerId,0)<>ISNULL(d.ResolvedByInventoryCostLayerId,0)) OR d.ReverseOfAllocationId IS NULL AND i.IsResolved<>CASE WHEN i.ResolvedQuantity+(SELECT ISNULL(SUM(a.Quantity),0) FROM InventoryCostLayerAllocations a WHERE a.StoreId=d.StoreId AND a.ReverseOfAllocationId=d.Id AND a.IsProvisional=1 AND a.IsDeleted=0)=i.Quantity THEN 1 ELSE 0 END OR d.InventoryCostLayerId IS NOT NULL AND ISNULL(i.InventoryCostLayerId,0)<>d.InventoryCostLayerId OR d.InventoryCostLayerId IS NULL AND i.InventoryCostLayerId IS NOT NULL AND (i.ResolvedQuantity<=d.ResolvedQuantity OR NOT EXISTS (SELECT 1 FROM InventoryCostLayers l WHERE l.StoreId=d.StoreId AND l.Id=i.InventoryCostLayerId AND l.WarehouseId=v.WarehouseId AND l.ProductVariantId=v.ProductVariantId AND l.CostBudgetPolicy=1 AND l.IsProvisionalSource=0 AND l.IsDeleted=0)) OR i.ResolvedQuantity>d.ResolvedQuantity AND (i.ResolvedByInventoryCostLayerId IS NULL OR NOT EXISTS (SELECT 1 FROM InventoryCostLayers l WHERE l.StoreId=d.StoreId AND l.Id=i.ResolvedByInventoryCostLayerId AND l.WarehouseId=v.WarehouseId AND l.ProductVariantId=v.ProductVariantId AND l.CostBudgetPolicy=1 AND l.IsProvisionalSource=0 AND l.IsDeleted=0)) OR d.ReverseOfAllocationId IS NOT NULL AND (i.ResolvedQuantity<>d.ResolvedQuantity OR i.ResolvedAmount<>d.ResolvedAmount OR i.IsResolved<>d.IsResolved OR ISNULL(i.ResolvedByInventoryCostLayerId,0)<>ISNULL(d.ResolvedByInventoryCostLayerId,0) OR ISNULL(i.InventoryCostLayerId,0)<>ISNULL(d.InventoryCostLayerId,0))))\n THROW 51041, 'Provisional delivery allocation estimates are immutable and resolution must preserve the unreturned source prefix.', 1;\nEND;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM DeliveryDispatchCostFragments WHERE IsProvisional=1 OR InventoryCostLayerId IS NULL OR InventoryCostBudgetDebitId IS NULL) OR EXISTS (SELECT 1 FROM DeliveryReturnedCostFragments WHERE IsProvisional=1) THROW 51040, 'Provisional delivery source proof is occupied; use a forward fix.', 1;");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryJournalEntries_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryDispatchCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryReturnedCostFragments_StockSource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryTransactions_DeliveryProvisional];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryValuationEntries_DeliveryProvisional];");

            migrationBuilder.Sql("DROP TRIGGER [TR_InventoryCostLayerAllocations_DeliveryProvisional];");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryJournalEntries_StockSource] ON [DeliveryJournalEntries] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN InventoryTransactions t ON t.StoreId=i.StoreId AND t.Id=i.InventoryTransactionId LEFT JOIN DeliveryOrderLines q ON q.StoreId=i.StoreId AND q.DeliveryOrderId=i.DeliveryOrderId AND q.Id=i.DeliveryOrderLineId WHERE (i.Kind=1 OR i.Kind=2) AND (t.Id IS NULL OR q.Id IS NULL OR q.VariantId<>i.VariantId OR t.WarehouseId<>i.SourceWarehouseId OR t.ProductVariantId<>i.VariantId OR t.PostingPrecisionPolicy<>2 OR t.ReferenceType<>11 OR t.ReferenceId<>CONVERT(nvarchar(64),i.DeliveryOrderId) OR t.ReferenceLineId<>i.DeliveryOrderLineId OR t.IsProvisionalCost=1 OR t.CostFinalizedAtUtc IS NULL OR t.IsDeleted=1 OR i.Kind=1 AND (t.TransactionType<>80 OR t.QuantityChange<>-i.BaseQuantity OR t.TotalCost<>-i.CostAmount) OR i.Kind=2 AND (t.TransactionType<>81 OR t.QuantityChange<>i.BaseQuantity OR t.TotalCost<>i.CostAmount)))\n THROW 51027, 'Stock journal must match its original delivery line and genuine Final movement.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryDispatchCostFragments_StockSource] ON [DeliveryDispatchCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryCostBudgetDebits b ON b.StoreId=i.StoreId AND b.Id=i.InventoryCostBudgetDebitId WHERE j.Kind<>1 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR b.IsProvisionalResolution=1 OR i.BaseQuantity<>b.EndQuantity-b.StartQuantity OR i.UnitCost<>b.UnitCost OR i.CostAmount<>b.Amount)\n THROW 51027, 'Dispatch fragment must use its exact ordinary stock debit/source journal.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId CROSS APPLY (SELECT SUM(f.BaseQuantity) Qty,SUM(f.CostAmount) Amount FROM DeliveryDispatchCostFragments f WITH (UPDLOCK,HOLDLOCK) WHERE f.StoreId=i.StoreId AND f.DeliveryJournalEntryId=j.Id) s WHERE s.Qty>j.BaseQuantity OR s.Amount>j.CostAmount)\n THROW 51027, 'Dispatch fragment quantity/value exceeds journal budget.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryReturnedCostFragments_StockSource] ON [DeliveryReturnedCostFragments] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId JOIN InventoryCostBudgetDebits b ON b.StoreId=f.StoreId AND b.Id=f.InventoryCostBudgetDebitId JOIN DeliveryJournalEntries j ON j.StoreId=i.StoreId AND j.Id=i.DeliveryJournalEntryId JOIN InventoryValuationEntries v ON v.StoreId=i.StoreId AND v.Id=i.InventoryValuationEntryId WHERE i.EndQuantity>f.BaseQuantity OR i.UnitCost<>f.UnitCost OR i.CostAmount<>CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.EndQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000))-CONVERT(decimal(18,4),((((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))-((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0)))/NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))+CASE WHEN ((CONVERT(decimal(38,0),(b.AnchorAmount)*10000)*CONVERT(decimal(38,0),(b.StartQuantity+i.StartQuantity)*10000))%NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0))*2>=NULLIF(CONVERT(decimal(38,0),(b.AnchorQuantity)*10000),0) THEN 1 ELSE 0 END)/CONVERT(decimal(19,4),10000)) OR j.Kind<>2 OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId<>i.VariantId OR j.InventoryTransactionId<>i.InventoryTransactionId OR j.BaseQuantity<>i.BaseQuantity OR j.CostAmount<>i.CostAmount OR v.EntryType<>1 OR v.SourceValuationEntryId IS NULL OR v.SourceValuationEntryId<>f.InventoryValuationEntryId OR v.Quantity<>i.BaseQuantity OR v.UnitCost<>i.UnitCost OR v.Amount<>i.CostAmount OR v.IsProvisional=1 OR v.CostFinalizedAtUtc IS NULL OR v.IsDeleted=1)\n THROW 51028, 'Accepted return must preserve its exact dispatch-tail cost and genuine inbound source.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.DispatchCostFragmentId=i.DispatchCostFragmentId AND r.Id<>i.Id AND r.StartQuantity<i.EndQuantity AND r.EndQuantity>i.StartQuantity)\n THROW 51028, 'Accepted return intervals cannot overlap.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments f ON f.StoreId=i.StoreId AND f.Id=i.DispatchCostFragmentId CROSS APPLY (SELECT MIN(r.StartQuantity) Start,SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=i.StoreId AND r.DispatchCostFragmentId=f.Id) s WHERE s.Start+s.Qty<>f.BaseQuantity)\n THROW 51028, 'Accepted returns must extend the tail toward the prefix without holes.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryDispatchCostFragments later ON later.StoreId=i.StoreId AND later.DeliveryOrderId=i.DeliveryOrderId AND later.DeliveryOrderLineId=i.DeliveryOrderLineId AND later.Id>i.DispatchCostFragmentId OUTER APPLY (SELECT SUM(r.BaseQuantity) Qty FROM DeliveryReturnedCostFragments r WITH (UPDLOCK,HOLDLOCK) WHERE r.StoreId=later.StoreId AND r.DispatchCostFragmentId=later.Id) s WHERE ISNULL(s.Qty,0)<>later.BaseQuantity)\n THROW 51028, 'Receive the latest dispatch fragment tail before an earlier source fragment.', 1;\nEND;");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryValuationEntries_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWareh~",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostLayerAllocationId_InventoryValuationEntryId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryValuationEntryId_InventoryTransactionId_SourceWarehouseId_VariantId",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryDispatchCostFragments_CostProof",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.DropColumn(
                name: "IsProvisional",
                table: "DeliveryReturnedCostFragments");

            migrationBuilder.DropColumn(
                name: "IsProvisional",
                table: "DeliveryDispatchCostFragments");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryCostLayerId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // Restore the historical defaults separately. AlterColumn(defaultValue: 0)
            // emits a backfill UPDATE, which is forbidden by the append-only trigger
            // even when no rows need changing. The Down guard already rejects NULLs.
            migrationBuilder.Sql("""
                ALTER TABLE [DeliveryDispatchCostFragments] ADD DEFAULT (0) FOR [InventoryCostLayerId];
                ALTER TABLE [DeliveryDispatchCostFragments] ADD DEFAULT (0) FOR [InventoryCostBudgetDebitId];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostBudgetDebitId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostBudgetDebitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId" },
                principalTable: "InventoryCostLayerAllocations",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}

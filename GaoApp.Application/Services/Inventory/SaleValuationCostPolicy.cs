using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Interprets supported durable sale-fragment histories without rewriting ledger signs.
/// A recorded amount is not a final cost when resolution or reversal provenance is unknown.
/// </summary>
public static class SaleValuationCostPolicy
{
    public enum Quality { Finalized, Provisional, PartiallyFinalized, Unavailable, DataIntegrityConflict }

    public sealed record Result(
        Quality State, decimal? Cost, decimal? ProvisionalExposure,
        decimal? FinalUnitCost, decimal InventoryReversedQuantity, string? Reason)
    {
        public string DisplayState => State switch
        {
            Quality.Finalized => "Đã xác định",
            Quality.Provisional or Quality.PartiallyFinalized => "Tạm tính",
            _ => "Chưa đủ dữ liệu"
        };

        public decimal RequireFinalUnitCost()
        {
            if (State != Quality.Finalized || FinalUnitCost is not > 0)
                throw new InvalidOperationException(
                    $"Không đủ lịch sử giá vốn đã xác định để đảo an toàn. {Reason}");
            return FinalUnitCost.Value;
        }
    }

    private static decimal Value(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    private static Result Unknown(string reason) => new(Quality.Unavailable, null, null, null, 0, reason);
    private static Result Conflict(string reason) => new(Quality.DataIntegrityConflict, null, null, null, 0, reason);

    public static Result Evaluate(
        InventoryValuationEntry source,
        IReadOnlyCollection<InventoryValuationEntry> revaluations,
        IReadOnlyCollection<InventoryValuationEntry> linkedEntries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(revaluations);
        ArgumentNullException.ThrowIfNull(linkedEntries);

        if (source.Id <= 0 || source.StoreId <= 0 || source.IsDeleted ||
            source.EntryType != InventoryValuationEntryType.Outbound || source.Quantity >= 0 ||
            source.ReferenceType != InventoryReferenceType.Order || source.ReferenceLineId is null ||
            source.UnitCost < 0 || source.Amount != Value(source.Quantity * source.UnitCost))
            return Conflict("Invalid original sale valuation.");

        if (source.Warehouse is null || source.ProductVariant is null)
            return Unknown("Original warehouse/variant evidence is missing.");
        if (source.Warehouse.Id != source.WarehouseId || source.Warehouse.StoreId != source.StoreId ||
            source.ProductVariant.Id != source.ProductVariantId || source.ProductVariant.StoreId != source.StoreId ||
            source.Warehouse.IsDeleted || source.ProductVariant.IsDeleted)
            return Conflict("Original warehouse/variant crosses the Store boundary.");

        var transaction = source.InventoryTransaction;
        if (transaction is null)
            return Unknown("Original sale transaction is missing.");
        if (transaction.Id != source.InventoryTransactionId || transaction.IsDeleted ||
            transaction.StoreId != source.StoreId || transaction.WarehouseId != source.WarehouseId ||
            transaction.ProductVariantId != source.ProductVariantId ||
            transaction.TransactionType != InventoryTransactionType.SaleIssue ||
            transaction.ReferenceType != InventoryReferenceType.Order ||
            transaction.ReferenceId != source.ReferenceId || transaction.ReferenceLineId != source.ReferenceLineId)
            return Conflict("Original sale transaction linkage does not match.");

        // Repeated query projections may repeat one entry; conflicting copies must not be hidden.
        var events = revaluations.Concat(linkedEntries).GroupBy(x => x.Id).ToList();
        foreach (var group in events)
        {
            var first = group.First();
            if (group.Key <= 0 || group.Any(x => !SameEvent(first, x)))
                return Conflict("Conflicting or missing durable event identity.");
        }

        var adjustments = events.Select(x => x.First())
            .Where(x => x.EntryType == InventoryValuationEntryType.Revaluation).ToList();
        var reversals = events.Select(x => x.First())
            .Where(x => x.EntryType != InventoryValuationEntryType.Revaluation).ToList();
        var families = new HashSet<InventoryCostSourceType>();
        var adjustment = 0m;
        foreach (var entry in adjustments)
        {
            if (!SameScope(source, entry) || entry.Quantity != 0 ||
                entry.RevaluationOfEntryId != source.Id || entry.ReferenceType != source.ReferenceType ||
                entry.ReferenceId != source.ReferenceId || entry.ReferenceLineId != source.ReferenceLineId)
                return Conflict("Revaluation source/scope mismatch.");
            if (entry.InventoryCostLayer is not { } layer || entry.InventoryTransaction is not { } owner)
                return Unknown("Revaluation layer/transaction evidence is missing.");
            if (layer.Id != entry.InventoryCostLayerId || layer.StoreId != source.StoreId ||
                layer.WarehouseId != source.WarehouseId || layer.ProductVariantId != source.ProductVariantId ||
                layer.IsDeleted || owner.IsDeleted || owner.Id != entry.InventoryTransactionId ||
                owner.StoreId != source.StoreId || owner.WarehouseId != source.WarehouseId ||
                owner.ProductVariantId != source.ProductVariantId)
                return Conflict("Revaluation owner/layer mismatch.");

            if (entry.CostSourceType == InventoryCostSourceType.Manual &&
                entry.SourceValuationEntryId == source.Id &&
                entry.SourceReferenceSubKey == source.ReferenceSubKey &&
                entry.ReferenceSubKey == source.ReferenceSubKey &&
                owner.Id == layer.InventoryTransactionId && owner.QuantityChange > 0)
                adjustment += entry.Amount;
            else if (entry.CostSourceType == InventoryCostSourceType.RevaluationAdjustment &&
                     entry.SourceValuationEntryId is null && owner.QuantityChange == 0 &&
                     source.CostLayerAllocations.Any(a =>
                         a.StoreId == source.StoreId && a.InventoryValuationEntryId == source.Id &&
                         entry.ReferenceSubKey == $"REVAL:L{layer.Id}:A{a.Id}"))
                adjustment -= entry.Amount;
            else
                return Unknown("Unsupported revaluation writer provenance.");
            families.Add(entry.CostSourceType);
        }
        if (families.Count > 1)
            return Unknown("Mixed resolution conventions require historical reconciliation.");

        var reversedQuantity = 0m;
        var reversedAmount = 0m;
        foreach (var entry in reversals)
        {
            if (!SameScope(source, entry) || entry.EntryType != InventoryValuationEntryType.Inbound ||
                entry.Quantity <= 0 || entry.SourceValuationEntryId != source.Id ||
                entry.RevaluationOfEntryId is not null || entry.SourceReferenceSubKey != source.ReferenceSubKey ||
                entry.Amount != Value(entry.Quantity * entry.UnitCost))
                return Conflict("Unsupported quantity reversal or linkage.");
            var owner = entry.InventoryTransaction;
            if (owner is null)
                return Unknown("Reversal transaction is missing.");
            if (owner.IsDeleted || owner.Id != entry.InventoryTransactionId ||
                owner.StoreId != source.StoreId || owner.WarehouseId != source.WarehouseId ||
                owner.ProductVariantId != source.ProductVariantId ||
                !((owner.TransactionType == InventoryTransactionType.SaleVoidIn &&
                   entry.ReferenceType == InventoryReferenceType.Order && entry.ReferenceId == source.ReferenceId &&
                   entry.ReferenceLineId == source.ReferenceLineId) ||
                  (owner.TransactionType == InventoryTransactionType.CustomerReturnIn &&
                   entry.ReferenceType == InventoryReferenceType.Refund)))
                return Conflict("Reversal is not a matching sale Void/Restock.");
            reversedQuantity += entry.Quantity;
            reversedAmount += entry.Amount;
        }

        var quantity = -source.Quantity;
        if (reversedQuantity > quantity)
            return Conflict("Inventory quantity was over-reversed.");
        var grossCost = -source.Amount + adjustment;
        if (grossCost < 0)
            return Conflict("Normalized sale cost is negative.");

        var allocations = source.CostLayerAllocations.Where(x => !x.IsDeleted).ToList();
        if (allocations.Count == 0)
            return Unknown("Original cost allocations are missing.");
        if (allocations.Any(x => x.StoreId != source.StoreId || x.InventoryValuationEntryId != source.Id))
            return Conflict("Allocation source/scope mismatch.");

        var finalizedAt = source.CostFinalizedAtUtc ?? source.OccurredAtUtc;
        if (source.IsProvisional)
        {
            if (allocations.Any(x => !x.IsProvisional))
                return Unknown("Mixed allocation provenance.");
            var dedicated = families.Contains(InventoryCostSourceType.RevaluationAdjustment);
            if (dedicated)
            {
                if (allocations.Any(x => !x.IsResolved || x.Quantity != 0) || source.CostFinalizedAtUtc is null)
                    return Unknown("Dedicated partial resolution has no supported final basis.");
                finalizedAt = source.CostFinalizedAtUtc.Value;
            }
            else
            {
                if (allocations.Sum(x => x.Quantity) != quantity ||
                    allocations.Any(x => x.ResolvedQuantity < 0 || x.ResolvedQuantity > x.Quantity))
                    return Conflict("Provisional resolution quantities do not reconcile.");
                var resolved = allocations.Sum(x => x.ResolvedQuantity);
                var resolvedCost = allocations.Sum(x => x.ResolvedAmount);
                var openCost = Value((quantity - resolved) * source.UnitCost);
                if (Value(resolvedCost + openCost) != grossCost)
                    return Unknown("Resolved cost does not reconcile to durable adjustments.");
                if (resolved < quantity)
                {
                    if (reversals.Count > 0)
                        return Unknown("Interleaved provisional reversal requires reconciliation.");
                    return new(resolved == 0 ? Quality.Provisional : Quality.PartiallyFinalized,
                        grossCost, openCost, null, 0, "Cost remains provisional.");
                }
                if (allocations.Any(x => !x.IsResolved || x.ResolvedAtUtc is null))
                    return Unknown("Final resolution evidence is incomplete.");
                finalizedAt = allocations.Max(x => x.ResolvedAtUtc!.Value);
            }
        }
        else if (adjustments.Count > 0 || allocations.Any(x => x.IsProvisional) ||
                 allocations.Sum(x => x.Quantity) != quantity)
            return Unknown("Unsupported actual-source resolution history.");

        if (reversals.Any(x => x.OccurredAtUtc < finalizedAt) ||
            adjustments.Any(x => x.OccurredAtUtc > finalizedAt))
            return Unknown("Reversal and finalization history is interleaved.");
        var unitCost = Math.Round(grossCost / quantity, 6, MidpointRounding.AwayFromZero);
        if (Value(unitCost * quantity) != grossCost)
            return Unknown("Final cost cannot be represented at movement precision.");
        if (reversals.Any(x => x.Amount != Value(x.Quantity * unitCost)))
            return Conflict("Historical reversal differs from normalized final cost.");
        var remaining = grossCost - reversedAmount;
        if (reversedQuantity == quantity && remaining != 0)
            return Conflict("Full reversal leaves a sale cost residual.");
        return new(Quality.Finalized, remaining, 0, unitCost, reversedQuantity, null);
    }

    private static bool SameScope(InventoryValuationEntry source, InventoryValuationEntry entry) =>
        !entry.IsDeleted && entry.StoreId == source.StoreId && entry.WarehouseId == source.WarehouseId &&
        entry.ProductVariantId == source.ProductVariantId;

    private static bool SameEvent(InventoryValuationEntry a, InventoryValuationEntry b) =>
        a.StoreId == b.StoreId && a.InventoryTransactionId == b.InventoryTransactionId &&
        a.WarehouseId == b.WarehouseId && a.ProductVariantId == b.ProductVariantId &&
        a.EntryType == b.EntryType && a.Quantity == b.Quantity && a.Amount == b.Amount &&
        a.UnitCost == b.UnitCost && a.RevaluationOfEntryId == b.RevaluationOfEntryId &&
        a.SourceValuationEntryId == b.SourceValuationEntryId && a.CostSourceType == b.CostSourceType &&
        a.ReferenceType == b.ReferenceType && a.ReferenceId == b.ReferenceId &&
        a.ReferenceLineId == b.ReferenceLineId && a.ReferenceSubKey == b.ReferenceSubKey &&
        a.SourceReferenceSubKey == b.SourceReferenceSubKey && a.InventoryCostLayerId == b.InventoryCostLayerId &&
        a.OccurredAtUtc == b.OccurredAtUtc && a.IsDeleted == b.IsDeleted;
}

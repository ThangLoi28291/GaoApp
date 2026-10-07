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
        decimal? FinalUnitCost, decimal InventoryReversedQuantity, string? Reason, bool SourceBudgetProtected = false)
    {
        public string DisplayState => State switch
        {
            Quality.Finalized => "Đã xác định",
            Quality.Provisional or Quality.PartiallyFinalized => "Tạm tính",
            _ => "Chưa đủ dữ liệu"
        };

        public decimal RequireFinalUnitCost()
        {
            if (SourceBudgetProtected) throw new InvalidOperationException("SOURCE_BUDGET_REVERSAL_UNSUPPORTED: nguồn ngân sách cần adapter đảo nguồn trước khi thay đổi kho/tiền.");
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
        if (source.InventoryTransaction?.PostingPrecisionPolicy is 1 or 2 || source.CostBudgetDebits.Count > 0)
            return EvaluateBudget(source, revaluations, linkedEntries);

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

    private static Result EvaluateBudget(InventoryValuationEntry source, IReadOnlyCollection<InventoryValuationEntry> revaluations,
        IReadOnlyCollection<InventoryValuationEntry> linkedEntries)
    {
        var tx = source.InventoryTransaction;
        if (source.Id <= 0 || source.StoreId <= 0 || source.IsDeleted || source.Quantity >= 0 ||
            source.ReferenceType != InventoryReferenceType.Order || source.ReferenceLineId is null || source.EntryType != InventoryValuationEntryType.Outbound ||
            tx is null || tx.IsDeleted || tx.Id != source.InventoryTransactionId || tx.StoreId != source.StoreId ||
            tx.WarehouseId != source.WarehouseId || tx.ProductVariantId != source.ProductVariantId || tx.TransactionType != InventoryTransactionType.SaleIssue ||
            tx.ReferenceType != source.ReferenceType || tx.ReferenceId != source.ReferenceId || tx.ReferenceLineId != source.ReferenceLineId ||
            source.Warehouse is not { } wh || wh.IsDeleted || wh.StoreId != source.StoreId || wh.Id != source.WarehouseId ||
            source.ProductVariant is not { } variant || variant.IsDeleted || variant.StoreId != source.StoreId || variant.Id != source.ProductVariantId)
            return Conflict("Budget sale source/scope is missing or invalid.");
        var allocations = source.CostLayerAllocations.Where(x => !x.IsDeleted).ToArray();
        var proofs = source.CostBudgetDebits.OrderBy(x => x.StartQuantity).ThenBy(x => x.Id).ToArray();
        if (allocations.Length == 0 || allocations.Any(x => x.StoreId != source.StoreId || x.InventoryValuationEntryId != source.Id || x.Quantity <= 0 || x.Amount < 0) ||
            allocations.Sum(x => x.Quantity) != -source.Quantity || allocations.Sum(x => x.Amount) != -source.Amount)
            return Conflict("Budget sale allocation quantities/amounts do not reconcile.");
        foreach (var proof in proofs)
        {
            if (proof.IsDeleted || proof.StoreId != source.StoreId || proof.WarehouseId != source.WarehouseId || proof.ProductVariantId != source.ProductVariantId ||
                proof.InventoryValuationEntryId != source.Id || proof.Layer is not { } layer || layer.IsDeleted || layer.Id != proof.InventoryCostLayerId ||
                layer.StoreId != source.StoreId || layer.WarehouseId != source.WarehouseId || layer.ProductVariantId != source.ProductVariantId || layer.CostBudgetPolicy != 1 ||
                layer.BudgetAnchorQuantity != proof.AnchorQuantity || layer.BudgetAnchorAmount != proof.AnchorAmount || proof.UnitCost != layer.UnitCost ||
                proof.SourceLegalEntityId != wh.LegalEntityId || proof.EndQuantity <= proof.StartQuantity || proof.StartQuantity < 0 || proof.EndQuantity > proof.AnchorQuantity ||
                !allocations.Any(x => x.Id == proof.InventoryCostLayerAllocationId))
                return Conflict("Budget sale layer/debit source chain does not match.");
            try { if (proof.Amount != InventoryCostBudgetPolicy.Slice(proof.AnchorQuantity, proof.AnchorAmount, proof.StartQuantity, proof.EndQuantity - proof.StartQuantity)) return Conflict("Budget debit amount is invalid."); }
            catch (GaoApp.Application.Common.Exceptions.BusinessRuleException) { return Conflict("Budget debit interval is invalid."); }
        }
        var children = revaluations.Concat(linkedEntries).GroupBy(x => x.Id).ToArray();
        if (children.Any(x => x.Key <= 0 || x.Any(y => !SameEvent(x.First(), y)))) return Conflict("Budget child identity conflicts.");
        var events = children.Select(x => x.First()).ToArray();
        if (events.Any(x => x.EntryType != InventoryValuationEntryType.Revaluation))
            return Unknown("Budget-source reversal requires the separately scoped source adapter.");
        if (!source.IsProvisional)
        {
            if (proofs.Length == 0 || proofs.Any(x => x.IsProvisionalResolution || x.InventoryTransactionId != source.InventoryTransactionId) || events.Length != 0 ||
                allocations.Any(x => x.IsProvisional) || proofs.Sum(x => x.EndQuantity - x.StartQuantity) != -source.Quantity || proofs.Sum(x => x.Amount) != -source.Amount)
                return Unknown("Final budget debit proof is incomplete.");
            foreach (var allocation in allocations)
            {
                var parts = proofs.Where(x => x.InventoryCostLayerAllocationId == allocation.Id).ToArray();
                if (parts.Length != 1 || allocation.InventoryCostLayerId != parts[0].InventoryCostLayerId || allocation.Quantity != parts[0].EndQuantity - parts[0].StartQuantity ||
                    allocation.Amount != parts[0].Amount || allocation.UnitCost != parts[0].UnitCost) return Conflict("Final budget allocation/debit mismatch.");
            }
            return new(Quality.Finalized, -source.Amount, 0, decimal.Round(-source.Amount / -source.Quantity, 6, MidpointRounding.AwayFromZero), 0, null, true);
        }
        if (allocations.Any(x => !x.IsProvisional) || proofs.Any(x => !x.IsProvisionalResolution)) return Conflict("Budget provisional subtype conflicts.");
        decimal finalCost = 0, exposure = 0, resolvedTotal = 0;
        var revaluationIds = new HashSet<int>();
        foreach (var allocation in allocations)
        {
            decimal cursor = 0, actualCost = 0;
            foreach (var proof in proofs.Where(x => x.InventoryCostLayerAllocationId == allocation.Id).OrderBy(x => x.ResolutionStartQuantity))
            {
                if (proof.ResolutionStartQuantity != cursor || proof.ResolutionEndQuantity is not decimal end || end <= cursor || end > allocation.Quantity ||
                    end - cursor != proof.EndQuantity - proof.StartQuantity) return Conflict("Budget resolution intervals have gaps/overlaps.");
                var provisional = InventoryCostBudgetPolicy.Slice(allocation.Quantity, allocation.Amount, cursor, end - cursor);
                var difference = provisional - proof.Amount;
                if (proof.RevaluationEntryId is int id)
                {
                    var entry = events.SingleOrDefault(x => x.Id == id);
                    if (entry is null || !SameScope(source, entry) || entry.Quantity != 0 || entry.Amount != difference || difference == 0 ||
                        entry.InventoryTransactionId != proof.InventoryTransactionId || entry.RevaluationOfEntryId != source.Id || entry.SourceValuationEntryId != source.Id ||
                        entry.InventoryCostLayerId != proof.InventoryCostLayerId || entry.InventoryTransaction is not { } owner ||
                        owner.TransactionType != InventoryTransactionType.Revaluation || owner.QuantityChange != 0 || owner.PostingPrecisionPolicy != 1 ||
                        owner.StoreId != source.StoreId || owner.WarehouseId != source.WarehouseId || owner.ProductVariantId != source.ProductVariantId || owner.TotalCost != difference)
                        return Conflict("Budget neutral revaluation does not match resolution.");
                    revaluationIds.Add(id);
                }
                else if (difference != 0) return Unknown("Budget nonzero resolution adjustment is missing.");
                cursor = end; actualCost += proof.Amount;
            }
            if (cursor != allocation.ResolvedQuantity || actualCost != allocation.ResolvedAmount || cursor > allocation.Quantity)
                return Unknown("Budget cumulative resolution proof is incomplete.");
            var open = InventoryCostBudgetPolicy.Slice(allocation.Quantity, allocation.Amount, cursor, allocation.Quantity - cursor);
            finalCost += actualCost + open; exposure += open; resolvedTotal += cursor;
            if (cursor == allocation.Quantity && (!allocation.IsResolved || allocation.ResolvedAtUtc is null)) return Unknown("Budget final resolution timestamp is missing.");
        }
        if (events.Any(x => !revaluationIds.Contains(x.Id))) return Conflict("Unproved budget revaluation child.");
        var state = resolvedTotal == -source.Quantity ? Quality.Finalized : resolvedTotal == 0 ? Quality.Provisional : Quality.PartiallyFinalized;
        if (state != Quality.Finalized && source.CostSourceType == InventoryCostSourceType.Unknown)
            return new(resolvedTotal == 0 ? Quality.Unavailable : Quality.PartiallyFinalized, null, null, null, 0, "Open provisional source cost is unknown.", true);
        return new(state, finalCost, exposure, state == Quality.Finalized ? decimal.Round(finalCost / -source.Quantity, 6, MidpointRounding.AwayFromZero) : null,
            0, state == Quality.Finalized ? null : "Cost remains provisional.", true);
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

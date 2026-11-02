using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public partial class InventoryMovementService
{
    public async Task<bool> TryCreatePurchaseReceiptBatchAsync(
        IReadOnlyList<CreateInventoryMovementRequest> requests, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count < 2 || requests.Any(x => x is null || x.QuantityChange <= 0 ||
                !x.UnitCost.HasValue || x.UnitCost <= 0 || !x.SkipIfExists || x.ProductVariantId <= 0 ||
                x.SourceValuationEntryId.HasValue || x.SourceReferenceSubKey is not null ||
                x.WarehouseId != requests[0].WarehouseId || x.ReferenceType != InventoryReferenceType.StockDocument ||
                x.TransactionType != InventoryTransactionType.PurchaseReceipt || string.IsNullOrWhiteSpace(x.ReferenceId)))
            return false;
        if (!_postingCoordinator.HasActiveTransaction)
            throw new InvalidOperationException("Receipt batch posting requires the caller's active transaction.");
        var warehouse = await _warehouseRepository.GetByIdAsync(requests[0].WarehouseId, ct)
            ?? throw new BusinessRuleException("Kho không tồn tại.");
        if (warehouse.StoreId <= 0) throw new BusinessRuleException("StoreId của kho không hợp lệ.");
        var balances = new Dictionary<int, InventoryBalance>();
        var provisionals = new Dictionary<int, List<ReceiptAllocationState>>();
        foreach (var variantId in requests.Select(x => x.ProductVariantId).Distinct().OrderBy(x => x))
        {
            var balance = await _balanceRepository.LockAndGetOrCreateAsync(warehouse.StoreId, warehouse.Id, variantId, ct);
            if (balance.QuantityPrecisionVersion != 0) return false;
            provisionals[variantId] = (await _allocationRepository.GetOpenProvisionalAllocationsForUpdateAsync(warehouse.Id, variantId, ct))
                .Select(x => new ReceiptAllocationState(x)).ToList();
            balances.Add(variantId, balance);
        }
        var identities = new HashSet<string>();
        var pending = new List<ReceiptPosting>();
        var running = balances.ToDictionary(x => x.Key,
            x => (Qty: RoundQty(x.Value.OnHandQty), Value: RoundValue(x.Value.InventoryValue),
                Average: ResolveBeforeAverageCost(x.Value, RoundQty(x.Value.OnHandQty), RoundValue(x.Value.InventoryValue))));
        foreach (var request in requests)
        {
            if (RoundQty(request.QuantityChange) == 0) return false;
            var identity = InventoryIdempotencyKeyFactory.Create(warehouse.StoreId, request);
            if (!identities.Add(Convert.ToHexString(identity)) ||
                await _transactionRepository.GetByIdempotencyKeyAsync(warehouse.StoreId, identity, ct) is not null ||
                await _transactionRepository.GetByLegacyIdentityAsync(warehouse.StoreId, warehouse.Id,
                    request.ProductVariantId, request.TransactionType, request.ReferenceType,
                    request.ReferenceId!, request.ReferenceLineId, request.ReferenceSubKey, ct) is not null)
                return false; // The existing engine validates and replays durable identities.
            await _sourceEvidence.RejectUnsupportedMirrorAsync(request, warehouse.StoreId, ct);
            var before = running[request.ProductVariantId];
            var occurred = request.OccurredAtUtc ?? DateTime.UtcNow;
            var inbound = BuildInboundEntry(request, 0, before.Qty, before.Value, before.Average, occurred);
            if (!request.AllowNegativeBalance && !warehouse.AllowNegativeInventory && inbound.entry.RunningQtyAfter < 0)
                throw new BusinessRuleException($"Kho '{warehouse.Name}' không cho âm tồn.");
            inbound.layer.RemainingOpenProvisionalQty = inbound.layer.OriginalQuantity;
            var remaining = inbound.layer.RemainingQuantity;
            var revaluations = new List<InventoryValuationEntry>();
            var lastEntry = inbound.entry;
            foreach (var state in provisionals[request.ProductVariantId])
            {
                if (remaining <= 0) break;
                if (state.IsResolved) continue;
                var allocation = state.Allocation;
                var open = RoundQty(allocation.Quantity - RoundQty(state.ResolvedQuantity));
                if (open <= 0) { state.IsResolved = true; continue; }
                var quantity = Math.Min(remaining, open);
                var actualCost = RoundCost(inbound.layer.UnitCost);
                var difference = RoundValue(RoundValue(quantity * actualCost) - RoundValue(quantity * RoundCost(allocation.UnitCost)));
                state.ResolvedQuantity = RoundQty(RoundQty(state.ResolvedQuantity) + quantity);
                state.ResolvedAmount = RoundValue(state.ResolvedAmount + quantity * actualCost);
                state.IsResolved = RoundQty(allocation.Quantity - state.ResolvedQuantity) <= 0;
                state.FirstLayer ??= inbound.layer; state.LastLayer = inbound.layer; state.ResolvedAt = occurred;
                if (difference != 0)
                {
                    var source = allocation.InventoryValuationEntry;
                    var value = RoundValue(lastEntry.RunningValueAfter + difference);
                    lastEntry = new InventoryValuationEntry
                    {
                        WarehouseId = warehouse.Id, ProductVariantId = request.ProductVariantId,
                        EntryType = InventoryValuationEntryType.Revaluation, ReferenceType = source.ReferenceType,
                        ReferenceId = source.ReferenceId, ReferenceLineId = source.ReferenceLineId, ReferenceSubKey = source.ReferenceSubKey,
                        SourceValuationEntryId = source.Id, SourceReferenceSubKey = source.ReferenceSubKey, RevaluationOfEntryId = source.Id,
                        Quantity = 0m, UnitCost = actualCost, Amount = difference,
                        RunningQtyAfter = inbound.entry.RunningQtyAfter, RunningValueAfter = value,
                        RunningAverageUnitCostAfter = CalculateAverageCost(inbound.entry.RunningQtyAfter, value),
                        CostSourceType = InventoryCostSourceType.Manual, CostFinalizedAtUtc = occurred, OccurredAtUtc = occurred,
                        Note = $"AUTO REVALUATION | AllocationId={allocation.Id} | ResolvedQty={quantity:N3}"
                    };
                    revaluations.Add(lastEntry);
                }
                remaining = RoundQty(remaining - quantity);
            }
            var transaction = new InventoryTransaction
            {
                StoreId = warehouse.StoreId, WarehouseId = warehouse.Id, ProductVariantId = request.ProductVariantId,
                IdempotencyKey = identity, TransactionType = request.TransactionType, ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId, ReferenceLineId = request.ReferenceLineId, ReferenceSubKey = request.ReferenceSubKey,
                QuantityChange = request.QuantityChange, BeforeQty = before.Qty, AfterQty = inbound.entry.RunningQtyAfter,
                UnitCostSnapshot = inbound.entry.UnitCost, TotalCost = RoundValue(inbound.entry.Amount + revaluations.Sum(x => x.Amount)),
                BeforeInventoryValue = before.Value, AfterInventoryValue = lastEntry.RunningValueAfter,
                RunningAverageUnitCostAfter = lastEntry.RunningAverageUnitCostAfter,
                CostSourceType = inbound.entry.CostSourceType, CostFinalizedAtUtc = occurred,
                OccurredAtUtc = occurred, Note = request.Note
            };
            pending.Add(new(request, transaction, inbound.entry, inbound.layer, revaluations, RoundQty(inbound.layer.OriginalQuantity - remaining)));
            running[request.ProductVariantId] = (lastEntry.RunningQtyAfter, lastEntry.RunningValueAfter, lastEntry.RunningAverageUnitCostAfter);
        }
        // Legacy inbounds, including partial provisional resolution, use this
        // path. Keep the same generated identities and back-links as
        // CreateAsync while saving all receipt lines together at each ID boundary.
        await _transactionRepository.AddRangeAsync(pending.Select(x => x.Transaction), ct);
        await _transactionRepository.SaveChangesAsync(ct);
        foreach (var item in pending)
        {
            item.Entry.InventoryTransactionId = item.Transaction.Id;
            item.Layer.InventoryTransactionId = item.Transaction.Id;
        }
        await _valuationRepository.AddRangeAsync(pending.Select(x => x.Entry), ct);
        await _transactionRepository.SaveChangesAsync(ct);
        foreach (var item in pending) item.Layer.InventoryValuationEntryId = item.Entry.Id;
        await _costLayerRepository.AddRangeAsync(pending.Select(x => x.Layer), ct);
        await _costLayerRepository.SaveChangesAsync(ct);
        foreach (var item in pending)
        {
            item.Entry.InventoryCostLayerId = item.Layer.Id;
            item.Layer.RemainingQuantity = RoundQty(item.Layer.OriginalQuantity - item.ResolvedQuantity);
            item.Layer.ResolvedProvisionalQty = item.ResolvedQuantity;
            item.Layer.RemainingOpenProvisionalQty = item.Layer.RemainingQuantity;
            foreach (var entry in item.Revaluations)
            {
                entry.InventoryTransactionId = item.Transaction.Id;
                entry.InventoryCostLayerId = item.Layer.Id;
            }
            await _valuationRepository.AddRangeAsync(item.Revaluations, ct);
            var balance = balances[item.Request.ProductVariantId];
            balance.OnHandQty = item.Transaction.AfterQty;
            balance.InventoryValue = item.Transaction.AfterInventoryValue;
            balance.AverageUnitCost = item.Transaction.RunningAverageUnitCostAfter;
            balance.LastValuationAtUtc = item.Entry.OccurredAtUtc;
            balance.LastInboundAtUtc = item.Entry.OccurredAtUtc;
            balance.LastInboundUnitCost = item.Entry.UnitCost;
        }
        foreach (var state in provisionals.Values.SelectMany(x => x))
        {
            var allocation = state.Allocation;
            allocation.ResolvedQuantity = state.ResolvedQuantity;
            allocation.ResolvedAmount = state.ResolvedAmount;
            allocation.IsResolved = state.IsResolved;
            if (state.LastLayer is not null)
            {
                allocation.InventoryCostLayerId ??= state.FirstLayer!.Id;
                allocation.ResolvedByInventoryCostLayerId = state.LastLayer.Id;
                allocation.ResolvedAtUtc = state.ResolvedAt;
            }
        }
        await _balanceRepository.SaveChangesAsync(ct);
        return true;
    }

    private sealed record ReceiptPosting(CreateInventoryMovementRequest Request, InventoryTransaction Transaction,
        InventoryValuationEntry Entry, InventoryCostLayer Layer, List<InventoryValuationEntry> Revaluations, decimal ResolvedQuantity);

    private sealed class ReceiptAllocationState(InventoryCostLayerAllocation allocation)
    {
        public InventoryCostLayerAllocation Allocation { get; } = allocation;
        public decimal ResolvedQuantity = allocation.ResolvedQuantity;
        public decimal ResolvedAmount = allocation.ResolvedAmount;
        public bool IsResolved = allocation.IsResolved;
        public InventoryCostLayer? FirstLayer;
        public InventoryCostLayer? LastLayer;
        public DateTime? ResolvedAt;
    }
}

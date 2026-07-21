using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public class InventoryRevaluationService : IInventoryRevaluationService
{
    private readonly IInventoryCostLayerRepository _layerRepository;
    private readonly IInventoryCostLayerAllocationRepository _allocationRepository;
    private readonly IInventoryValuationEntryRepository _valuationRepository;
    private readonly IInventoryTransactionRepository _transactionRepository;
    private readonly IInventoryBalanceRepository _balanceRepository;

    public InventoryRevaluationService(
        IInventoryCostLayerRepository layerRepository,
        IInventoryCostLayerAllocationRepository allocationRepository,
        IInventoryValuationEntryRepository valuationRepository,
        IInventoryTransactionRepository transactionRepository,
        IInventoryBalanceRepository balanceRepository)
    {
        _layerRepository = layerRepository;
        _allocationRepository = allocationRepository;
        _valuationRepository = valuationRepository;
        _transactionRepository = transactionRepository;
        _balanceRepository = balanceRepository;
    }

    public async Task<List<ProvisionalRevaluationPlanDto>> ResolveByInboundLayerAsync(
        int inboundLayerId,
        DateTime occurredAtUtc,
        string note,
        CancellationToken ct = default)
    {
        var inboundLayer = await _layerRepository.GetByIdAsync(inboundLayerId, ct)
            ?? throw new InvalidOperationException("Inbound layer không tồn tại.");

        if (inboundLayer.RemainingQuantity <= 0)
            return new List<ProvisionalRevaluationPlanDto>();

        var balance = await _balanceRepository.GetOrCreateAsync(
            inboundLayer.WarehouseId,
            inboundLayer.ProductVariantId,
            ct);

        var openProvisionals = await _allocationRepository.GetOpenProvisionalAllocationsForUpdateAsync(
            inboundLayer.WarehouseId,
            inboundLayer.ProductVariantId,
            ct);

        var result = new List<ProvisionalRevaluationPlanDto>();
        var remainingInbound = inboundLayer.RemainingQuantity;

        foreach (var provisional in openProvisionals)
        {
            if (remainingInbound <= 0)
                break;

            var resolveQty = Math.Min(provisional.Quantity, remainingInbound);
            if (resolveQty <= 0)
                continue;

            var provisionalEntry = provisional.InventoryValuationEntry;

            var delta = inboundLayer.UnitCost - provisional.UnitCost;
            var revaluationAmount = Math.Round(-(resolveQty * delta), 4);

            var beforeQty = balance.OnHandQty;
            var beforeValue = balance.InventoryValue;

            var afterValue = beforeValue + revaluationAmount;
            var afterAvg = beforeQty != 0
                ? Math.Round(afterValue / beforeQty, 6)
                : 0m;

            var tx = new InventoryTransaction
            {
                StoreId = provisional.StoreId,
                WarehouseId = inboundLayer.WarehouseId,
                ProductVariantId = inboundLayer.ProductVariantId,
                TransactionType = InventoryTransactionType.PurchaseReceipt,
                ReferenceType = provisionalEntry.ReferenceType,
                ReferenceId = provisionalEntry.ReferenceId,
                ReferenceLineId = provisionalEntry.ReferenceLineId,
                QuantityChange = 0,
                BeforeQty = beforeQty,
                AfterQty = beforeQty,
                OccurredAtUtc = occurredAtUtc,
                Note = note
            };

            await _transactionRepository.AddAsync(tx, ct);
            await _transactionRepository.SaveChangesAsync(ct);

            var entry = new InventoryValuationEntry
            {
                StoreId = provisional.StoreId,
                InventoryTransactionId = tx.Id,
                WarehouseId = inboundLayer.WarehouseId,
                ProductVariantId = inboundLayer.ProductVariantId,

                EntryType = InventoryValuationEntryType.Revaluation,
                ReferenceType = provisionalEntry.ReferenceType,
                ReferenceId = provisionalEntry.ReferenceId,
                ReferenceLineId = provisionalEntry.ReferenceLineId,

                ReferenceSubKey = $"REVAL:L{inboundLayer.Id}:A{provisional.Id}",

                Quantity = 0,
                UnitCost = inboundLayer.UnitCost,
                Amount = revaluationAmount,

                RunningQtyAfter = beforeQty,
                RunningValueAfter = afterValue,
                RunningAverageUnitCostAfter = afterAvg,

                CostSourceType = InventoryCostSourceType.RevaluationAdjustment,
                IsProvisional = false,
                CostFinalizedAtUtc = occurredAtUtc,
                RevaluationOfEntryId = provisionalEntry.Id,
                InventoryCostLayerId = inboundLayer.Id,
                Note = note,
                OccurredAtUtc = occurredAtUtc
            };

            await _valuationRepository.AddRangeAsync(new[] { entry }, ct);

            // ===== UPDATE ALLOCATION =====
            provisional.Quantity -= resolveQty;
            provisional.Amount = -(provisional.Quantity * provisional.UnitCost);

            provisional.IsResolved = provisional.Quantity <= 0;
            provisional.ResolvedAtUtc = provisional.IsResolved ? occurredAtUtc : null;
            provisional.ResolvedByInventoryCostLayerId = inboundLayer.Id;

            // ===== UPDATE LAYER =====
            inboundLayer.RemainingQuantity -= resolveQty;
            remainingInbound = inboundLayer.RemainingQuantity;

            // ===== FINALIZE ENTRY =====
            if (provisional.IsResolved)
                provisionalEntry.CostFinalizedAtUtc = occurredAtUtc;

            // ===== UPDATE BALANCE =====
            balance.InventoryValue = afterValue;
            balance.AverageUnitCost = afterAvg;
            balance.LastValuationAtUtc = occurredAtUtc;

            result.Add(new ProvisionalRevaluationPlanDto
            {
                ProvisionalEntryId = provisionalEntry.Id,
                ProvisionalAllocationId = provisional.Id,
                InboundLayerId = inboundLayer.Id,
                QuantityAbs = resolveQty,
                ProvisionalUnitCost = provisional.UnitCost,
                FinalUnitCost = inboundLayer.UnitCost,
                UnitCostDelta = delta,
                RevaluationAmount = revaluationAmount
            });
        }

        await _allocationRepository.SaveChangesAsync(ct);
        await _layerRepository.SaveChangesAsync(ct);
        await _balanceRepository.SaveChangesAsync(ct);
        await _transactionRepository.SaveChangesAsync(ct);

        return result;
    }
}
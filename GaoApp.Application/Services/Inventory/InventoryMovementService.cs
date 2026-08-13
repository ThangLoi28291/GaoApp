using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Core service ghi movement tồn kho:
/// - ghi InventoryTransaction (ledger qty)
/// - ghi InventoryValuationEntry (ledger value)
/// - cập nhật InventoryBalance (qty/value/avg)
///
/// Nguyên tắc:
/// 1) append-only cho transaction + valuation entry
/// 2) không rewrite history
/// 3) nếu âm kho thì tạo provisional valuation đúng cách
/// 4) Application không phụ thuộc Infrastructure/AppDbContext
///
/// Ghi chú mức 2:
/// - outbound actual phải tách theo FIFO layer thật sự
/// - allocation được tạo SAU KHI valuation entry đã save để có entry.Id
/// - không dùng field tạm như Tag trên entity
/// </summary>
public class InventoryMovementService : IInventoryMovementService
{
    private readonly IInventoryBalanceRepository _balanceRepository;
    private readonly IInventoryTransactionRepository _transactionRepository;
    private readonly IInventoryValuationEntryRepository _valuationRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IInventoryCostLayerRepository _costLayerRepository;
    private readonly IInventoryCostLayerAllocationRepository _allocationRepository;
    private readonly IInventoryPostingTransactionCoordinator _postingCoordinator;

    public InventoryMovementService(
        IInventoryBalanceRepository balanceRepository,
        IInventoryTransactionRepository transactionRepository,
        IInventoryValuationEntryRepository valuationRepository,
        IInventoryCostLayerRepository costLayerRepository,
        IInventoryCostLayerAllocationRepository allocationRepository,
        IWarehouseRepository warehouseRepository,
        IInventoryPostingTransactionCoordinator postingCoordinator)
    {
        _balanceRepository = balanceRepository;
        _transactionRepository = transactionRepository;
        _valuationRepository = valuationRepository;
        _costLayerRepository = costLayerRepository;
        _allocationRepository = allocationRepository;
        _warehouseRepository = warehouseRepository;
        _postingCoordinator = postingCoordinator;
    }

    public async Task PreLockBalancesAsync(
        IEnumerable<InventoryPostingLockKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var materializedKeys = keys.ToList();
        foreach (var key in materializedKeys)
        {
            if (key is null)
            {
                throw new ArgumentException(
                    "Inventory posting lock keys cannot contain null values.",
                    nameof(keys));
            }

            if (key.StoreId <= 0
                || key.WarehouseId <= 0
                || key.ProductVariantId <= 0)
            {
                throw new BusinessRuleException(
                    "Inventory posting lock identity values must be greater than zero.");
            }
        }

        var orderedKeys = materializedKeys
            .Distinct()
            .OrderBy(x => x.StoreId)
            .ThenBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductVariantId)
            .ToList();

        if (orderedKeys.Count == 0)
        {
            return;
        }

        if (!_postingCoordinator.HasActiveTransaction)
        {
            throw new InvalidOperationException(
                "Inventory balance batch pre-locking requires an active database transaction.");
        }

        var warehouseStores = new Dictionary<int, int>();
        foreach (var warehouseId in orderedKeys
                     .Select(x => x.WarehouseId)
                     .Distinct()
                     .OrderBy(x => x))
        {
            var warehouse = await _warehouseRepository
                .GetByIdAsync(warehouseId, ct);
            if (warehouse is null)
            {
                throw new BusinessRuleException(
                    $"Inventory posting warehouse {warehouseId} does not exist.");
            }

            warehouseStores.Add(warehouseId, warehouse.StoreId);
        }

        foreach (var key in orderedKeys)
        {
            if (warehouseStores[key.WarehouseId] != key.StoreId)
            {
                throw new BusinessRuleException(
                    $"Inventory posting warehouse {key.WarehouseId} does not belong to store {key.StoreId}.");
            }
        }

        foreach (var key in orderedKeys)
        {
            await _balanceRepository.LockAndGetOrCreateAsync(
                key.StoreId,
                key.WarehouseId,
                key.ProductVariantId,
                ct);
        }
    }

    public async Task<InventoryMovementResultDto> CreateAsync(
        CreateInventoryMovementRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.WarehouseId <= 0)
            throw new BusinessRuleException("WarehouseId không hợp lệ.");

        if (request.ProductVariantId <= 0)
            throw new BusinessRuleException("ProductVariantId không hợp lệ.");

        if (request.QuantityChange == 0)
            throw new BusinessRuleException("QuantityChange phải khác 0.");

        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId, ct)
            ?? throw new BusinessRuleException("Kho không tồn tại.");

        if (warehouse.StoreId <= 0)
            throw new BusinessRuleException("StoreId của kho không hợp lệ.");

        return await _postingCoordinator.ExecuteAsync(
            operationCt => CreateWithinTransactionAsync(
                request,
                warehouse,
                warehouse.StoreId,
                operationCt),
            ct);
    }

    private async Task<InventoryMovementResultDto> CreateWithinTransactionAsync(
        CreateInventoryMovementRequest request,
        Warehouse warehouse,
        int storeId,
        CancellationToken ct)
    {
        var balance = await _balanceRepository.LockAndGetOrCreateAsync(
            storeId,
            request.WarehouseId,
            request.ProductVariantId,
            ct);

        byte[]? idempotencyKey = null;
        if (request.SkipIfExists)
        {
            idempotencyKey = InventoryIdempotencyKeyFactory.Create(
                storeId,
                request);

            var existing = await _transactionRepository
                .GetByIdempotencyKeyAsync(
                    storeId,
                    idempotencyKey,
                    ct);

            if (existing is not null)
            {
                await VerifyIdempotentPayloadAsync(
                    existing,
                    storeId,
                    request,
                    ct);
                return BuildSkippedResult(existing, request);
            }

            var legacy = await _transactionRepository
                .GetByLegacyIdentityAsync(
                    storeId,
                    request.WarehouseId,
                    request.ProductVariantId,
                    request.TransactionType,
                    request.ReferenceType,
                    request.ReferenceId!,
                    request.ReferenceLineId,
                    request.ReferenceSubKey,
                    ct);

            if (legacy is not null)
            {
                await VerifyIdempotentPayloadAsync(
                    legacy,
                    storeId,
                    request,
                    ct);
                return BuildSkippedResult(legacy, request);
            }
        }

        var occurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow;

        var beforeQty = RoundQty(balance.OnHandQty);
        var beforeValue = RoundValue(balance.InventoryValue);

        var beforeAverageCost = ResolveBeforeAverageCost(balance, beforeQty, beforeValue);
        var afterQtyByTransaction = RoundQty(beforeQty + request.QuantityChange);

        if (!request.AllowNegativeBalance
            && !warehouse.AllowNegativeInventory
            && afterQtyByTransaction < 0)
        {
            throw new BusinessRuleException(
                $"Kho '{warehouse.Name}' không cho âm tồn. " +
                $"BeforeQty={beforeQty}, Change={request.QuantityChange}, AfterQty={afterQtyByTransaction}.");
        }

        var transaction = new InventoryTransaction
        {
            StoreId = storeId,
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,
            IdempotencyKey = idempotencyKey,
            TransactionType = request.TransactionType,
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId,
            ReferenceLineId = request.ReferenceLineId,
            ReferenceSubKey = request.ReferenceSubKey,
            QuantityChange = request.QuantityChange,
            BeforeQty = beforeQty,
            AfterQty = afterQtyByTransaction,
            UnitCostSnapshot = request.UnitCost.HasValue
                ? RoundCost(request.UnitCost.Value)
                : 0m,
            OccurredAtUtc = occurredAtUtc,
            Note = request.Note
        };

        await _transactionRepository.AddAsync(transaction, ct);
        await _transactionRepository.SaveChangesAsync(ct);

        List<InventoryValuationEntry> valuationEntries;
        InventoryValuationEntry lastEntry;

        if (request.QuantityChange > 0)
        {
            // =====================================================
            // INBOUND
            // =====================================================
            var inbound = BuildInboundEntry(
                request,
                transaction.Id,
                beforeQty,
                beforeValue,
                beforeAverageCost,
                occurredAtUtc);

            valuationEntries = new List<InventoryValuationEntry> { inbound.entry };

            await _valuationRepository.AddRangeAsync(valuationEntries, ct);
            await _transactionRepository.SaveChangesAsync(ct);

            // Sau khi entry đã có Id mới gắn ngược sang layer
            inbound.layer.InventoryValuationEntryId = inbound.entry.Id;

            await _costLayerRepository.AddAsync(inbound.layer, ct);
            await _costLayerRepository.SaveChangesAsync(ct);

            inbound.entry.InventoryCostLayerId = inbound.layer.Id;
            await _transactionRepository.SaveChangesAsync(ct);

            var remainingLayerQty = RoundQty(inbound.layer.RemainingQuantity);
            var resolvedQty = 0m;

            var revaluationEntries = new List<InventoryValuationEntry>();
            var runningQtyForRevaluation = inbound.entry.RunningQtyAfter;
            var runningValueForRevaluation = inbound.entry.RunningValueAfter;

            if (remainingLayerQty > 0)
            {
                var openProvisionalAllocations =
                    await _allocationRepository.GetOpenProvisionalAllocationsForUpdateAsync(
                        request.WarehouseId,
                        request.ProductVariantId,
                        ct);

                foreach (var allocation in openProvisionalAllocations)
                {
                    if (remainingLayerQty <= 0)
                        break;

                    if (!allocation.IsProvisional || allocation.IsResolved)
                        continue;

                    var allocationResolvedQtyBefore = RoundQty(allocation.ResolvedQuantity);
                    var allocationOpenQty = RoundQty(allocation.Quantity - allocationResolvedQtyBefore);

                    if (allocationOpenQty <= 0)
                    {
                        allocation.IsResolved = true;
                        continue;
                    }

                    var qtyToResolve = Math.Min(remainingLayerQty, allocationOpenQty);
                    if (qtyToResolve <= 0)
                        continue;

                    // Giữ nguyên provisional cost gốc trên allocation.
                    var provisionalUnitCost = RoundCost(allocation.UnitCost);
                    var actualUnitCost = RoundCost(inbound.layer.UnitCost);

                    allocation.InventoryCostLayerId ??= inbound.layer.Id;
                    allocation.ResolvedByInventoryCostLayerId = inbound.layer.Id;
                    allocation.ResolvedAtUtc = occurredAtUtc;

                    allocation.ResolvedQuantity = RoundQty(allocationResolvedQtyBefore + qtyToResolve);
                    allocation.ResolvedAmount = RoundValue(
                        allocation.ResolvedAmount + (qtyToResolve * actualUnitCost));

                    if (RoundQty(allocation.Quantity - allocation.ResolvedQuantity) <= 0)
                    {
                        allocation.IsResolved = true;
                    }

                    // =========================
                    // AUTO REVALUATION CHO PHẦN VỪA RESOLVE
                    // =========================
                    var provisionalAmountForResolvedPart = RoundValue(qtyToResolve * provisionalUnitCost);
                    var actualAmountForResolvedPart = RoundValue(qtyToResolve * actualUnitCost);
                    var diffAmount = RoundValue(actualAmountForResolvedPart - provisionalAmountForResolvedPart);

                    if (diffAmount != 0)
                    {
                        runningValueForRevaluation = RoundValue(runningValueForRevaluation + diffAmount);
                        var runningAvgAfterRevaluation = CalculateAverageCost(
                            runningQtyForRevaluation,
                            runningValueForRevaluation);

                        var sourceEntry = allocation.InventoryValuationEntry;

                        var revaluationEntry = new InventoryValuationEntry
                        {
                            InventoryTransactionId = transaction.Id,
                            WarehouseId = request.WarehouseId,
                            ProductVariantId = request.ProductVariantId,

                            EntryType = InventoryValuationEntryType.Revaluation,

                            // Revaluation phải bám order/order line gốc phát sinh provisional
                            ReferenceType = sourceEntry.ReferenceType,
                            ReferenceId = sourceEntry.ReferenceId,
                            ReferenceLineId = sourceEntry.ReferenceLineId,
                            ReferenceSubKey = sourceEntry.ReferenceSubKey,

                            SourceValuationEntryId = sourceEntry.Id,
                            SourceReferenceSubKey = sourceEntry.ReferenceSubKey,
                            RevaluationOfEntryId = sourceEntry.Id,

                            Quantity = 0m,
                            UnitCost = actualUnitCost,
                            Amount = diffAmount,

                            RunningQtyAfter = runningQtyForRevaluation,
                            RunningValueAfter = runningValueForRevaluation,
                            RunningAverageUnitCostAfter = runningAvgAfterRevaluation,

                            CostSourceType = InventoryCostSourceType.Manual,
                            IsProvisional = false,
                            CostFinalizedAtUtc = occurredAtUtc,
                            InventoryCostLayerId = inbound.layer.Id,
                            Note = $"AUTO REVALUATION | AllocationId={allocation.Id} | ResolvedQty={qtyToResolve:N3}",
                            OccurredAtUtc = occurredAtUtc
                        };

                        revaluationEntries.Add(revaluationEntry);
                    }

                    remainingLayerQty = RoundQty(remainingLayerQty - qtyToResolve);
                    resolvedQty = RoundQty(resolvedQty + qtyToResolve);
                }

                inbound.layer.RemainingQuantity = remainingLayerQty;
                inbound.layer.ResolvedProvisionalQty = RoundQty(
                    inbound.layer.ResolvedProvisionalQty + resolvedQty);
                inbound.layer.RemainingOpenProvisionalQty = RoundQty(
                    inbound.layer.OriginalQuantity - inbound.layer.ResolvedProvisionalQty);

                await _allocationRepository.SaveChangesAsync(ct);
                await _costLayerRepository.SaveChangesAsync(ct);

                if (revaluationEntries.Count > 0)
                {
                    await _valuationRepository.AddRangeAsync(revaluationEntries, ct);
                    await _transactionRepository.SaveChangesAsync(ct);
                    valuationEntries.AddRange(revaluationEntries);
                }
            }
            else
            {
                inbound.layer.RemainingOpenProvisionalQty = RoundQty(
                    inbound.layer.OriginalQuantity - inbound.layer.ResolvedProvisionalQty);

                await _costLayerRepository.SaveChangesAsync(ct);
            }

            lastEntry = valuationEntries[^1];
        }
        else
        {
            // =====================================================
            // OUTBOUND
            // - actual outbound được tách theo FIFO layer
            // - provisional outbound tạo riêng nếu âm kho
            // =====================================================
            valuationEntries = BuildValuationEntries(
                request,
                transaction.Id,
                balance,
                beforeQty,
                beforeValue,
                beforeAverageCost,
                occurredAtUtc,
                ct);

            if (valuationEntries.Count == 0)
                throw new InvalidOperationException("Movement không sinh được InventoryValuationEntry.");

            // Save valuation trước để entry có Id
            await _valuationRepository.AddRangeAsync(valuationEntries, ct);
            await _transactionRepository.SaveChangesAsync(ct);

            // =====================================================
            // Tạo allocation sau khi entry đã có Id
            // Đây là chỗ đúng để tạo mapping FIFO layer <-> valuation entry
            // =====================================================
            foreach (var entry in valuationEntries)
            {
                // Actual outbound: mỗi entry đã tương ứng với đúng 1 FIFO layer
                if (!entry.IsProvisional
                    && entry.EntryType == InventoryValuationEntryType.Outbound
                    && entry.InventoryCostLayerId.HasValue
                    && entry.Quantity < 0)
                {
                    var actualAllocation = new InventoryCostLayerAllocation
                    {
                        InventoryValuationEntryId = entry.Id,
                        InventoryCostLayerId = entry.InventoryCostLayerId.Value,
                        Quantity = RoundQty(Math.Abs(entry.Quantity)),
                        UnitCost = RoundCost(entry.UnitCost),
                        Amount = RoundValue(Math.Abs(entry.Quantity) * entry.UnitCost),

                        IsProvisional = false,
                        IsResolved = true,
                        ResolvedQuantity = RoundQty(Math.Abs(entry.Quantity)),
                        ResolvedAmount = RoundValue(Math.Abs(entry.Quantity) * entry.UnitCost),
                        ResolvedAtUtc = occurredAtUtc,
                        ResolvedByInventoryCostLayerId = entry.InventoryCostLayerId.Value,

                        Note = $"FIFO consume layer {entry.InventoryCostLayerId.Value}"
                    };

                    await _allocationRepository.AddAsync(actualAllocation, ct);
                }

                // Provisional outbound: chưa có cost layer thật nên để layer null
                if (entry.IsProvisional
                    && entry.EntryType == InventoryValuationEntryType.Outbound
                    && entry.Quantity < 0)
                {
                    var provisionalAllocation = new InventoryCostLayerAllocation
                    {
                        InventoryValuationEntryId = entry.Id,
                        InventoryCostLayerId = null,
                        Quantity = RoundQty(Math.Abs(entry.Quantity)),
                        UnitCost = RoundCost(entry.UnitCost),
                        Amount = RoundValue(Math.Abs(entry.Quantity) * entry.UnitCost),

                        IsProvisional = true,
                        IsResolved = false,
                        ResolvedQuantity = 0m,
                        ResolvedAmount = 0m,
                        ResolvedAtUtc = null,
                        ResolvedByInventoryCostLayerId = null,

                        Note = "Open provisional allocation"
                    };

                    await _allocationRepository.AddAsync(provisionalAllocation, ct);
                }
            }

            await _allocationRepository.SaveChangesAsync(ct);

            lastEntry = valuationEntries[^1];
        }

        // =====================================================
        // Cập nhật balance snapshot cuối
        // =====================================================
        balance.OnHandQty = lastEntry.RunningQtyAfter;
        balance.InventoryValue = lastEntry.RunningValueAfter;
        balance.AverageUnitCost = lastEntry.RunningAverageUnitCostAfter;
        balance.LastValuationAtUtc = lastEntry.OccurredAtUtc;

        var transactionValueChange = RoundValue(
            valuationEntries.Sum(x => x.Amount));
        if (!request.UnitCost.HasValue && request.QuantityChange != 0)
        {
            transaction.UnitCostSnapshot = RoundCost(
                Math.Abs(transactionValueChange / request.QuantityChange));
        }

        transaction.TotalCost = transactionValueChange;
        transaction.BeforeInventoryValue = beforeValue;
        transaction.AfterInventoryValue = lastEntry.RunningValueAfter;
        transaction.RunningAverageUnitCostAfter =
            lastEntry.RunningAverageUnitCostAfter;
        transaction.CostSourceType = lastEntry.CostSourceType;
        transaction.IsProvisionalCost =
            valuationEntries.Any(x => x.IsProvisional);
        transaction.CostFinalizedAtUtc =
            transaction.IsProvisionalCost
                ? null
                : lastEntry.CostFinalizedAtUtc;

        if (request.QuantityChange > 0 && !InboundLikeLastEntryIsProvisional(valuationEntries))
        {
            var inboundEntry = valuationEntries.FirstOrDefault(x => x.EntryType == InventoryValuationEntryType.Inbound);
            if (inboundEntry != null)
            {
                balance.LastInboundUnitCost = inboundEntry.UnitCost;
                balance.LastInboundAtUtc = inboundEntry.OccurredAtUtc;
            }
        }

        await _balanceRepository.SaveChangesAsync(ct);

        var provisionalEntries = valuationEntries
            .Where(x => x.IsProvisional)
            .ToList();

        var actualEntries = valuationEntries
            .Where(x => !x.IsProvisional)
            .ToList();

        var totalValueChange = RoundValue(valuationEntries.Sum(x => x.Amount));
        var provisionalValueChange = RoundValue(provisionalEntries.Sum(x => x.Amount));
        var actualValueChange = RoundValue(actualEntries.Sum(x => x.Amount));

        var provisionalQuantity = RoundQty(provisionalEntries.Sum(x => Math.Abs(x.Quantity)));
        var actualQuantity = RoundQty(actualEntries.Sum(x => Math.Abs(x.Quantity)));

        return new InventoryMovementResultDto
        {
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,

            BeforeQty = beforeQty,
            QuantityChange = request.QuantityChange,
            AfterQty = lastEntry.RunningQtyAfter,

            BeforeValue = beforeValue,
            ValueChange = totalValueChange,
            AfterValue = lastEntry.RunningValueAfter,

            BeforeAverageCost = beforeAverageCost,
            AfterAverageCost = lastEntry.RunningAverageUnitCostAfter,

            HasProvisionalValuation = provisionalEntries.Count > 0,

            ProvisionalQuantity = provisionalQuantity,
            ProvisionalUnitCost = provisionalEntries.Count > 0
                ? provisionalEntries.Last().UnitCost
                : 0m,
            ProvisionalValueChange = provisionalValueChange,

            ActualQuantity = actualQuantity,
            ActualValueChange = actualValueChange,

            InventoryTransactionId = transaction.Id,
            InventoryValuationEntryIds = valuationEntries.Select(x => x.Id).ToList(),

            IsNegativeAfterTransaction = lastEntry.RunningQtyAfter < 0,
            IsCreated = true,
            IsSkipped = false
        };
    }

    private async Task VerifyIdempotentPayloadAsync(
        InventoryTransaction existing,
        int storeId,
        CreateInventoryMovementRequest request,
        CancellationToken ct)
    {
        if (!InventoryIdempotencyKeyFactory.HasSameCanonicalIdentity(
                existing,
                storeId,
                request)
            || RoundQty(existing.QuantityChange)
                != RoundQty(request.QuantityChange))
        {
            throw new ConcurrencyException(
                "The durable inventory posting identity already exists with a different payload.");
        }

        if (!request.UnitCost.HasValue)
        {
            return;
        }

        var persistedUnitCost = existing.IdempotencyKey is not null
            || existing.UnitCostSnapshot != 0m
            || request.UnitCost.Value == 0m
                ? existing.UnitCostSnapshot
                : await ResolveLegacyUnitCostAsync(
                    existing,
                    storeId,
                    ct);

        if (!persistedUnitCost.HasValue
            || RoundCost(persistedUnitCost.Value)
                != RoundCost(request.UnitCost.Value))
        {
            throw new ConcurrencyException(
                "The durable inventory posting identity already exists with a different unit cost.");
        }
    }

    private async Task<decimal?> ResolveLegacyUnitCostAsync(
        InventoryTransaction transaction,
        int storeId,
        CancellationToken ct)
    {
        var entries = await _valuationRepository
            .GetByInventoryTransactionIdAsync(
                storeId,
                transaction.Id,
                ct);

        var inbound = entries.FirstOrDefault(
            x => x.EntryType == InventoryValuationEntryType.Inbound
                && x.Quantity != 0m);
        if (inbound is not null)
        {
            return inbound.UnitCost;
        }

        var quantityEntries = entries
            .Where(x => x.Quantity != 0m)
            .ToList();
        var totalQuantity = quantityEntries.Sum(
            x => Math.Abs(x.Quantity));
        if (totalQuantity == 0m)
        {
            return null;
        }

        return Math.Abs(quantityEntries.Sum(x => x.Amount))
            / totalQuantity;
    }

    private static InventoryMovementResultDto BuildSkippedResult(
        InventoryTransaction existing,
        CreateInventoryMovementRequest request)
    {
        return new InventoryMovementResultDto
        {
            WarehouseId = existing.WarehouseId,
            ProductVariantId = existing.ProductVariantId,
            BeforeQty = existing.BeforeQty,
            QuantityChange = request.QuantityChange,
            AfterQty = existing.AfterQty,
            IsNegativeAfterTransaction = existing.AfterQty < 0,
            IsCreated = false,
            IsSkipped = true,
            BeforeValue = existing.BeforeInventoryValue,
            ValueChange = existing.TotalCost,
            AfterValue = existing.AfterInventoryValue,
            AfterAverageCost = existing.RunningAverageUnitCostAfter,
            HasProvisionalValuation = existing.IsProvisionalCost,
            ProvisionalUnitCost = existing.IsProvisionalCost
                ? existing.UnitCostSnapshot
                : 0m
        };
    }

    /// <summary>
    /// Build valuation entries theo 3 case:
    /// 1) inbound
    /// 2) outbound nhưng không âm
    /// 3) outbound làm âm => tách actual + provisional
    /// </summary>
    private List<InventoryValuationEntry> BuildValuationEntries(
        CreateInventoryMovementRequest request,
        int inventoryTransactionId,
        InventoryBalance balance,
        decimal beforeQty,
        decimal beforeValue,
        decimal beforeAverageCost,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var entries = new List<InventoryValuationEntry>();

        if (request.QuantityChange > 0)
        {
            var inbound = BuildInboundEntry(
                request,
                inventoryTransactionId,
                beforeQty,
                beforeValue,
                beforeAverageCost,
                occurredAtUtc);

            entries.Add(inbound.entry);
            return entries;
        }

        var outboundQtyAbs = Math.Abs(request.QuantityChange);

        // Phần issue được từ tồn thật hiện có
        var actualQty = beforeQty > 0
            ? Math.Min(beforeQty, outboundQtyAbs)
            : 0m;

        // Phần vượt quá tồn thật => âm kho => provisional
        var provisionalQty = outboundQtyAbs - actualQty;

        if (actualQty > 0)
        {
            // Hàm này đang sync nên tạm dùng GetAwaiter/GetResult như hiện tại của bạn.
            var openLayers = _costLayerRepository
                .GetOpenLayersForUpdateAsync(request.WarehouseId, request.ProductVariantId, ct)
                .GetAwaiter()
                .GetResult();

            var actualEntries = BuildOutboundActualEntriesFifo(
                request,
                inventoryTransactionId,
                beforeQty,
                beforeValue,
                occurredAtUtc,
                openLayers,
                actualQty);

            if (actualEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    "Không lấy được FIFO layer thực tế cho outbound actual.");
            }

            entries.AddRange(actualEntries);

            var lastActualEntry = actualEntries[^1];

            // cập nhật running snapshot trung gian
            beforeQty = lastActualEntry.RunningQtyAfter;
            beforeValue = lastActualEntry.RunningValueAfter;
            beforeAverageCost = lastActualEntry.RunningAverageUnitCostAfter;
        }

        if (provisionalQty > 0)
        {
            if (!request.ForceProvisionalWhenNegative)
            {
                throw new InvalidOperationException(
                    "Movement làm âm kho nhưng request không cho tạo provisional valuation.");
            }

            var provisionalEntry = BuildOutboundProvisionalEntry(
                request,
                inventoryTransactionId,
                balance,
                beforeQty,
                beforeValue,
                beforeAverageCost,
                occurredAtUtc,
                provisionalQty);

            entries.Add(provisionalEntry);
        }

        return entries;
    }

    /// <summary>
    /// Tạo các outbound ACTUAL entries theo FIFO layer.
    ///
    /// Quan trọng:
    /// - hàm này chỉ split valuation entry theo từng layer
    /// - KHÔNG tạo allocation tại đây vì entry chưa có Id
    /// - allocation sẽ được tạo ở CreateAsync sau khi valuationEntries đã SaveChanges
    /// </summary>
    private List<InventoryValuationEntry> BuildOutboundActualEntriesFifo(
        CreateInventoryMovementRequest request,
        int inventoryTransactionId,
        decimal beforeQty,
        decimal beforeValue,
        DateTime occurredAtUtc,
        List<InventoryCostLayer> openLayers,
        decimal actualQty)
    {
        var entries = new List<InventoryValuationEntry>();

        if (actualQty <= 0)
            return entries;

        var remainingToIssue = RoundQty(actualQty);
        var runningQty = beforeQty;
        var runningValue = beforeValue;
        var partNo = 0;

        foreach (var layer in openLayers
                     .OrderBy(x => x.OccurredAtUtc)
                     .ThenBy(x => x.Id))
        {
            if (remainingToIssue <= 0)
                break;

            if (layer.RemainingQuantity <= 0)
                continue;

            var issueQty = Math.Min(layer.RemainingQuantity, remainingToIssue);
            if (issueQty <= 0)
                continue;

            partNo++;

            var quantity = RoundQty(-issueQty);
            var unitCost = RoundCost(layer.UnitCost);
            var amount = RoundValue(-(issueQty * unitCost));

            runningQty = RoundQty(runningQty + quantity);
            runningValue = RoundValue(runningValue + amount);
            var runningAvg = CalculateAverageCost(runningQty, runningValue);

            var subKey = string.IsNullOrWhiteSpace(request.ReferenceSubKey)
                ? $"LAYER:{layer.Id}:PART:{partNo}"
                : $"{request.ReferenceSubKey}:LAYER:{layer.Id}:PART:{partNo}";

            var entry = new InventoryValuationEntry
            {
                InventoryTransactionId = inventoryTransactionId,
                WarehouseId = request.WarehouseId,
                ProductVariantId = request.ProductVariantId,

                EntryType = InventoryValuationEntryType.Outbound,
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId ?? string.Empty,
                ReferenceLineId = request.ReferenceLineId,
                ReferenceSubKey = subKey,

                // Với sale return / reverse flow, các field Source* này
                // giúp trace ngược về source fragment gốc nếu request truyền xuống.
                SourceValuationEntryId = request.SourceValuationEntryId,
                SourceReferenceSubKey = request.SourceReferenceSubKey,

                Quantity = quantity,
                UnitCost = unitCost,
                Amount = amount,

                RunningQtyAfter = runningQty,
                RunningValueAfter = runningValue,
                RunningAverageUnitCostAfter = runningAvg,

                CostSourceType = InventoryCostSourceType.Manual,
                IsProvisional = false,
                CostFinalizedAtUtc = occurredAtUtc,
                InventoryCostLayerId = layer.Id,
                Note = request.Note,
                OccurredAtUtc = occurredAtUtc
            };

            entries.Add(entry);

            // Trừ quantity khỏi FIFO layer ngay khi layer được consume.
            layer.RemainingQuantity = RoundQty(layer.RemainingQuantity - issueQty);

            remainingToIssue = RoundQty(remainingToIssue - issueQty);
        }

        return entries;
    }

    /// <summary>
    /// Inbound:
    /// amount = qty * unitCost
    /// runningQtyAfter = beforeQty + qty
    /// runningValueAfter = beforeValue + amount
    /// runningAvgAfter = runningValueAfter / runningQtyAfter
    ///
    /// QUAN TRỌNG:
    /// - Với sales return mức 2, inbound mirror từ source fragment gốc
    /// - nên phải giữ SourceValuationEntryId + SourceReferenceSubKey
    /// - để các lần return sau biết fragment nào đã reverse rồi
    /// </summary>
    private (InventoryValuationEntry entry, InventoryCostLayer layer) BuildInboundEntry(
        CreateInventoryMovementRequest request,
        int inventoryTransactionId,
        decimal beforeQty,
        decimal beforeValue,
        decimal beforeAverageCost,
        DateTime occurredAtUtc)
    {
        if (!request.UnitCost.HasValue || request.UnitCost.Value <= 0)
            throw new BusinessRuleException("Movement nhập phải có UnitCost hợp lệ.");

        var qty = RoundQty(request.QuantityChange);
        var unitCost = RoundCost(request.UnitCost.Value);
        var amount = RoundValue(qty * unitCost);

        var runningQtyAfter = RoundQty(beforeQty + qty);
        var runningValueAfter = RoundValue(beforeValue + amount);
        var runningAvgAfter = CalculateAverageCost(runningQtyAfter, runningValueAfter);

        var entry = new InventoryValuationEntry
        {
            InventoryTransactionId = inventoryTransactionId,
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,

            EntryType = InventoryValuationEntryType.Inbound,
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId ?? string.Empty,
            ReferenceLineId = request.ReferenceLineId,

            // Giữ trace fragment hiện tại
            ReferenceSubKey = request.ReferenceSubKey,

            // QUAN TRỌNG NHẤT:
            // Với sales return / reverse inbound, phải giữ link về source fragment gốc
            SourceValuationEntryId = request.SourceValuationEntryId,
            SourceReferenceSubKey = request.SourceReferenceSubKey,

            Quantity = qty,
            UnitCost = unitCost,
            Amount = amount,

            RunningQtyAfter = runningQtyAfter,
            RunningValueAfter = runningValueAfter,
            RunningAverageUnitCostAfter = runningAvgAfter,

            CostSourceType = InventoryCostSourceType.Manual,
            IsProvisional = false,
            CostFinalizedAtUtc = occurredAtUtc,
            Note = request.Note,
            OccurredAtUtc = occurredAtUtc
        };

        var layer = new InventoryCostLayer
        {
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,
            InventoryTransactionId = inventoryTransactionId,

            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId ?? string.Empty,
            ReferenceLineId = request.ReferenceLineId,

            OriginalQuantity = qty,
            RemainingQuantity = qty,
            UnitCost = unitCost,
            OccurredAtUtc = occurredAtUtc,
            Note = request.Note
        };

        return (entry, layer);
    }

    /// <summary>
    /// Outbound provisional:
    /// áp cho phần quantity vượt quá tồn thật.
    ///
    /// quantity = -provisionalQty
    /// amount = -(provisionalQty * provisionalUnitCost)
    /// runningQtyAfter = beforeQty - provisionalQty
    /// runningValueAfter = beforeValue - provisionalAmount
    ///
    /// Không rewrite history.
    /// Sau này nếu cost thật khác cost tạm, phải tạo Revaluation entry.
    /// </summary>
    private InventoryValuationEntry BuildOutboundProvisionalEntry(
        CreateInventoryMovementRequest request,
        int inventoryTransactionId,
        InventoryBalance balance,
        decimal beforeQty,
        decimal beforeValue,
        decimal beforeAverageCost,
        DateTime occurredAtUtc,
        decimal provisionalQty)
    {
        var (provisionalUnitCost, costSourceType) = ResolveProvisionalCostInfo(
            request,
            balance,
            beforeAverageCost);

        if (provisionalUnitCost <= 0)
        {
            throw new BusinessRuleException(
                "Không xác định được provisional cost hợp lệ cho phần xuất âm kho. " +
                "Sản phẩm có thể chưa từng nhập kho và chưa có CostPrice nền.");
        }

        var quantity = RoundQty(-provisionalQty);
        var amount = RoundValue(-(provisionalQty * provisionalUnitCost));

        var runningQtyAfter = RoundQty(beforeQty + quantity);
        var runningValueAfter = RoundValue(beforeValue + amount);
        var runningAvgAfter = CalculateAverageCost(runningQtyAfter, runningValueAfter);

        return new InventoryValuationEntry
        {
            InventoryTransactionId = inventoryTransactionId,
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,

            EntryType = InventoryValuationEntryType.Outbound,
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId ?? string.Empty,
            ReferenceLineId = request.ReferenceLineId,

            // Trace fragment hiện tại
            ReferenceSubKey = request.ReferenceSubKey,
            SourceValuationEntryId = request.SourceValuationEntryId,
            SourceReferenceSubKey = request.SourceReferenceSubKey,

            Quantity = quantity,
            UnitCost = provisionalUnitCost,
            Amount = amount,

            RunningQtyAfter = runningQtyAfter,
            RunningValueAfter = runningValueAfter,
            RunningAverageUnitCostAfter = runningAvgAfter,

            CostSourceType = costSourceType,
            IsProvisional = true,
            CostFinalizedAtUtc = null,
            RevaluationOfEntryId = null,
            Note = request.Note,
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Ưu tiên snapshot AverageUnitCost trên balance.
    /// Fallback về value / qty nếu snapshot chưa có.
    /// </summary>
    private static decimal ResolveBeforeAverageCost(
        InventoryBalance balance,
        decimal beforeQty,
        decimal beforeValue)
    {
        if (balance.AverageUnitCost > 0)
            return RoundCost(balance.AverageUnitCost);

        return CalculateAverageCost(beforeQty, beforeValue);
    }

    /// <summary>
    /// Resolve provisional cost theo thứ tự ưu tiên:
    /// 1) request.ProvisionalUnitCost => Manual
    /// 2) balance.LastInboundUnitCost => LastInboundCost
    /// 3) beforeAverageCost => MovingAverage
    /// 4) balance.AverageUnitCost => MovingAverage
    /// 5) request.UnitCost => Manual
    /// 6) Unknown nếu vẫn không tìm được
    /// </summary>
    private static (decimal cost, InventoryCostSourceType sourceType) ResolveProvisionalCostInfo(
        CreateInventoryMovementRequest request,
        InventoryBalance balance,
        decimal beforeAverageCost)
    {
        if (request.ProvisionalUnitCost.HasValue && request.ProvisionalUnitCost.Value > 0)
        {
            return (RoundCost(request.ProvisionalUnitCost.Value), InventoryCostSourceType.Manual);
        }

        if (balance.LastInboundUnitCost.HasValue && balance.LastInboundUnitCost.Value > 0)
        {
            return (RoundCost(balance.LastInboundUnitCost.Value), InventoryCostSourceType.LastInboundCost);
        }

        if (beforeAverageCost > 0)
        {
            return (RoundCost(beforeAverageCost), InventoryCostSourceType.MovingAverage);
        }

        if (balance.AverageUnitCost > 0)
        {
            return (RoundCost(balance.AverageUnitCost), InventoryCostSourceType.MovingAverage);
        }

        if (request.UnitCost.HasValue && request.UnitCost.Value > 0)
        {
            return (RoundCost(request.UnitCost.Value), InventoryCostSourceType.Manual);
        }

        return (0m, InventoryCostSourceType.Unknown);
    }

    /// <summary>
    /// Average cost = value / qty nếu qty != 0, ngược lại = 0.
    /// qty âm + value âm => avg vẫn dương.
    /// </summary>
    private static decimal CalculateAverageCost(decimal qty, decimal value)
    {
        if (qty == 0)
            return 0m;

        return RoundCost(value / qty);
    }

    private static decimal RoundQty(decimal value)
        => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static decimal RoundCost(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundValue(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    public async Task<decimal> PeekOutboundUnitCostAsync(
        int warehouseId,
        int productVariantId,
        decimal quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return 0m;

        var remaining = RoundQty(quantity);
        decimal totalQty = 0m;
        decimal totalCost = 0m;

        // Lấy FIFO layers còn hàng.
        // Dù không có layer vẫn không return sớm,
        // vì còn phải fallback về balance cost nếu có.
        var layers = await _costLayerRepository.GetOpenLayersForUpdateAsync(
            warehouseId,
            productVariantId,
            ct);

        if (layers != null && layers.Count > 0)
        {
            foreach (var layer in layers.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id))
            {
                if (remaining <= 0)
                    break;

                if (layer.RemainingQuantity <= 0)
                    continue;

                var takeQty = Math.Min(layer.RemainingQuantity, remaining);
                if (takeQty <= 0)
                    continue;

                totalQty = RoundQty(totalQty + takeQty);
                totalCost = RoundValue(totalCost + (takeQty * layer.UnitCost));
                remaining = RoundQty(remaining - takeQty);
            }
        }

        // Nếu chưa đủ qty từ open layer thì fallback theo cost snapshot của balance.
        // Ưu tiên:
        // 1) AverageUnitCost dương
        // 2) LastInboundUnitCost dương
        if (remaining > 0)
        {
            var balance = await _balanceRepository.GetOrCreateAsync(
                warehouseId,
                productVariantId,
                ct);

            var fallbackCost = balance.AverageUnitCost > 0
                ? RoundCost(balance.AverageUnitCost)
                : (balance.LastInboundUnitCost.HasValue && balance.LastInboundUnitCost.Value > 0
                    ? RoundCost(balance.LastInboundUnitCost.Value)
                    : 0m);

            if (fallbackCost <= 0)
                return 0m;

            totalQty = RoundQty(totalQty + remaining);
            totalCost = RoundValue(totalCost + (remaining * fallbackCost));
        }

        if (totalQty <= 0)
            return 0m;

        return RoundCost(totalCost / totalQty);
    }

    private static bool InboundLikeLastEntryIsProvisional(List<InventoryValuationEntry> entries)
    {
        var inboundEntry = entries.FirstOrDefault(x => x.EntryType == InventoryValuationEntryType.Inbound);
        return inboundEntry != null && inboundEntry.IsProvisional;
    }
}

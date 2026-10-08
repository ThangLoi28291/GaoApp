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
    private readonly IInventoryReservationRepository _reservationRepository;
    private readonly IInventorySourceCostEvidenceRepository _sourceEvidence;

    public InventoryMovementService(
        IInventoryBalanceRepository balanceRepository,
        IInventoryTransactionRepository transactionRepository,
        IInventoryValuationEntryRepository valuationRepository,
        IInventoryCostLayerRepository costLayerRepository,
        IInventoryCostLayerAllocationRepository allocationRepository,
        IWarehouseRepository warehouseRepository,
        IInventoryPostingTransactionCoordinator postingCoordinator,
        IInventoryReservationRepository reservationRepository,
        IInventorySourceCostEvidenceRepository sourceEvidence)
    {
        _balanceRepository = balanceRepository;
        _transactionRepository = transactionRepository;
        _valuationRepository = valuationRepository;
        _costLayerRepository = costLayerRepository;
        _allocationRepository = allocationRepository;
        _warehouseRepository = warehouseRepository;
        _postingCoordinator = postingCoordinator;
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        _sourceEvidence = sourceEvidence ?? throw new ArgumentNullException(nameof(sourceEvidence));
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
        if (request.ReferenceType == InventoryReferenceType.Delivery || request.TransactionType is InventoryTransactionType.DeliveryDispatchOut or InventoryTransactionType.DeliveryReturnIn)
            throw new BusinessRuleException("TYPED_SOURCE_REQUIRED: bút toán giao hàng phải dùng nguồn xác thực.");

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

    public async Task<InventoryMovementResultDto> CreateRevaluationAsync(
        int warehouseId,
        int productVariantId,
        decimal quantityBase,
        decimal correctedUnitCost,
        string referenceId,
        int referenceLineId,
        string? note,
        DateTime? occurredAtUtc = null,
        CancellationToken ct = default)
    {
        if (warehouseId <= 0 || productVariantId <= 0 || referenceLineId <= 0)
            throw new BusinessRuleException("Thông tin điều chỉnh giá vốn không hợp lệ.");
        if (quantityBase <= 0 || correctedUnitCost <= 0)
            throw new BusinessRuleException("Số lượng và giá vốn điều chỉnh phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(referenceId))
            throw new BusinessRuleException("Thiếu chứng từ nguồn của điều chỉnh giá vốn.");

        var warehouse = await _warehouseRepository.GetByIdAsync(warehouseId, ct)
            ?? throw new BusinessRuleException("Kho không tồn tại.");

        return await _postingCoordinator.ExecuteAsync(
            operationCt => CreateRevaluationWithinTransactionAsync(
                warehouse, productVariantId, quantityBase, correctedUnitCost,
                referenceId, referenceLineId, note, occurredAtUtc ?? DateTime.UtcNow,
                operationCt),
            ct);
    }

    private async Task<InventoryMovementResultDto> CreateRevaluationWithinTransactionAsync(
        Warehouse warehouse,
        int productVariantId,
        decimal quantityBase,
        decimal correctedUnitCost,
        string referenceId,
        int referenceLineId,
        string? note,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var balance = await _balanceRepository.LockAndGetOrCreateAsync(
            warehouse.StoreId, warehouse.Id, productVariantId, ct);
        var beforeQty = balance.QuantityPrecisionVersion == 1 ? InventoryPostingPrecisionPolicy.Quantity(balance.OnHandQty) : RoundQty(balance.OnHandQty);
        if (beforeQty <= 0)
            throw new BusinessRuleException(
                "Không còn tồn dương để điều chỉnh giá vốn. Nếu hàng đã bán, cần lập nghiệp vụ revaluation theo phần giá vốn đã xuất.");

        var qty = balance.QuantityPrecisionVersion == 1 ? InventoryPostingPrecisionPolicy.EffectiveDelta(quantityBase, false) : RoundQty(quantityBase);
        if (qty > beforeQty)
            throw new BusinessRuleException(
                $"Số lượng điều chỉnh giá vốn ({qty:0.###}) không được vượt tồn hiện tại ({beforeQty:0.###}).");

        var oldAverageCost = balance.QuantityPrecisionVersion == 1
            ? decimal.Round(balance.InventoryValue / beforeQty, 6, MidpointRounding.AwayFromZero)
            : ResolveBeforeAverageCost(balance, beforeQty, balance.InventoryValue);
        var targetCost = balance.QuantityPrecisionVersion == 1 ? InventoryPostingPrecisionPolicy.Cost(correctedUnitCost) : RoundCost(correctedUnitCost);
        var amount = RoundValue(qty * (targetCost - oldAverageCost));
        var referenceSubKey = "COST-REVALUATION";

        var existing = await _transactionRepository.GetByLegacyIdentityAsync(
            warehouse.StoreId, warehouse.Id, productVariantId,
            InventoryTransactionType.Revaluation, InventoryReferenceType.Adjustment,
            referenceId, referenceLineId, referenceSubKey, ct);
        if (existing is not null)
            return new InventoryMovementResultDto
            {
                WarehouseId = warehouse.Id,
                ProductVariantId = productVariantId,
                BeforeQty = existing.BeforeQty,
                QuantityChange = 0m,
                AfterQty = existing.AfterQty,
                BeforeValue = existing.BeforeInventoryValue,
                ValueChange = existing.TotalCost,
                AfterValue = existing.AfterInventoryValue,
                BeforeAverageCost = oldAverageCost,
                AfterAverageCost = existing.RunningAverageUnitCostAfter,
                InventoryTransactionId = existing.Id,
                IsCreated = false,
                IsSkipped = true
            };

        if (balance.QuantityPrecisionVersion == 1)
            throw new BusinessRuleException(
                "SOURCE_BUDGET_REVALUATION_UNSUPPORTED: điều chỉnh giá vốn nguồn ngân sách cần adapter chia khoảng nguồn; chưa thay đổi kho/tiền.");

        var beforeValue = RoundValue(balance.InventoryValue);
        var afterValue = RoundValue(beforeValue + amount);
        var afterAverageCost = balance.QuantityPrecisionVersion == 1 ? decimal.Round(afterValue / beforeQty, 6, MidpointRounding.AwayFromZero) : CalculateAverageCost(beforeQty, afterValue);
        var transaction = new InventoryTransaction
        {
            StoreId = warehouse.StoreId,
            WarehouseId = warehouse.Id,
            ProductVariantId = productVariantId,
            TransactionType = InventoryTransactionType.Revaluation,
            PostingPrecisionPolicy = balance.QuantityPrecisionVersion,
            ReferenceType = InventoryReferenceType.Adjustment,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            ReferenceSubKey = referenceSubKey,
            QuantityChange = 0m,
            BeforeQty = beforeQty,
            AfterQty = beforeQty,
            UnitCostSnapshot = targetCost,
            TotalCost = amount,
            BeforeInventoryValue = beforeValue,
            AfterInventoryValue = afterValue,
            RunningAverageUnitCostAfter = afterAverageCost,
            CostSourceType = InventoryCostSourceType.RevaluationAdjustment,
            IsProvisionalCost = false,
            CostFinalizedAtUtc = occurredAtUtc,
            OccurredAtUtc = occurredAtUtc,
            Note = string.IsNullOrWhiteSpace(note)
                ? $"Điều chỉnh giá vốn {qty:0.###} đơn vị gốc"
                : note.Trim()
        };
        await _transactionRepository.AddAsync(transaction, ct);
        await _transactionRepository.SaveChangesAsync(ct);

        var entry = new InventoryValuationEntry
        {
            StoreId = warehouse.StoreId,
            InventoryTransactionId = transaction.Id,
            WarehouseId = warehouse.Id,
            ProductVariantId = productVariantId,
            EntryType = InventoryValuationEntryType.Revaluation,
            ReferenceType = InventoryReferenceType.Adjustment,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            ReferenceSubKey = referenceSubKey,
            Quantity = 0m,
            UnitCost = targetCost,
            Amount = amount,
            RunningQtyAfter = beforeQty,
            RunningValueAfter = afterValue,
            RunningAverageUnitCostAfter = afterAverageCost,
            CostSourceType = InventoryCostSourceType.RevaluationAdjustment,
            IsProvisional = false,
            CostFinalizedAtUtc = occurredAtUtc,
            Note = transaction.Note,
            OccurredAtUtc = occurredAtUtc
        };
        await _valuationRepository.AddRangeAsync([entry], ct);
        balance.InventoryValue = afterValue;
        balance.AverageUnitCost = afterAverageCost;
        balance.LastValuationAtUtc = occurredAtUtc;
        await _transactionRepository.SaveChangesAsync(ct);
        await _balanceRepository.SaveChangesAsync(ct);

        return new InventoryMovementResultDto
        {
            WarehouseId = warehouse.Id,
            ProductVariantId = productVariantId,
            BeforeQty = beforeQty,
            QuantityChange = 0m,
            AfterQty = beforeQty,
            BeforeValue = beforeValue,
            ValueChange = amount,
            AfterValue = afterValue,
            BeforeAverageCost = oldAverageCost,
            AfterAverageCost = afterAverageCost,
            InventoryTransactionId = transaction.Id,
            InventoryValuationEntryIds = [entry.Id],
            ActualValueChange = amount,
            IsCreated = true
        };
    }

    private async Task<InventoryMovementResultDto> CreateWithinTransactionAsync(
        CreateInventoryMovementRequest request,
        Warehouse warehouse,
        int storeId,
        CancellationToken ct,
        InventoryDeliveryPostingProof? trusted = null,
        bool strictFinal = false)
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
        if (request.ReferenceType != InventoryReferenceType.Delivery && request.QuantityChange != 0 && RoundQty(request.QuantityChange) == 0)
            throw new BusinessRuleException("BELOW_LEGACY_STEP: số lượng khác không nhưng nhỏ hơn bước xuất kho cũ.");
        await _sourceEvidence.RejectUnsupportedMirrorAsync(request, storeId, ct);
        var protectedRows = await _reservationRepository.GetActiveForBalanceAsync(storeId, request.WarehouseId, request.ProductVariantId, ct);
        var protectedQuantity = protectedRows.Where(x => x.ReferenceType == InventoryReferenceType.Delivery && x.Id != trusted?.OwnReservationId).Sum(x => x.ReservedQty);
        if (request.ReferenceType == InventoryReferenceType.Delivery)
        {
            if (trusted is null || trusted.StoreId != storeId || trusted.WarehouseId != request.WarehouseId ||
                trusted.VariantId != request.ProductVariantId || trusted.Quantity != Math.Abs(request.QuantityChange))
                throw new BusinessRuleException("TYPED_SOURCE_REQUIRED: nguồn giao hàng không khớp.");
            await _sourceEvidence.EnsurePromotedAsync(balance, ct);
        }
        if (balance.QuantityPrecisionVersion == 1)
        {
            await _sourceEvidence.EnsurePromotedAsync(balance, ct);
            return await CreatePromotedAsync(request, warehouse, balance, idempotencyKey, protectedQuantity, trusted, strictFinal, occurredAtUtc, ct);
        }
        if (request.QuantityChange < 0 && RoundQty(RoundQty(balance.OnHandQty) + request.QuantityChange) < protectedQuantity && protectedQuantity > 0)
            throw new BusinessRuleException("DELIVERY_STOCK_PROTECTED: hàng đang được soạn cho đơn giao.");

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
            || (existing.PostingPrecisionPolicy == 2 ? existing.QuantityChange : RoundQty(existing.QuantityChange))
                != (existing.PostingPrecisionPolicy == 2 ? request.QuantityChange : RoundQty(request.QuantityChange)))
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
            || (existing.PostingPrecisionPolicy == 2 ? persistedUnitCost.Value : RoundCost(persistedUnitCost.Value))
                != (existing.PostingPrecisionPolicy == 2 ? request.UnitCost.Value : RoundCost(request.UnitCost.Value)))
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

    public async Task<InventoryMovementResultDto> CreateDeliveryDispatchAsync(InventoryDeliveryDispatchPosting input, CancellationToken ct = default)
    {
        if (!_postingCoordinator.HasActiveTransaction) throw new InvalidOperationException("Delivery dispatch requires its caller transaction.");
        var proof = await _sourceEvidence.GetDeliveryDispatchProofAsync(input, ct);
        var warehouse = await _warehouseRepository.GetByIdAsync(proof.WarehouseId, ct) ?? throw new BusinessRuleException("Kho nguồn không tồn tại.");
        var request = new CreateInventoryMovementRequest { WarehouseId = proof.WarehouseId, ProductVariantId = proof.VariantId,
            ReferenceType = InventoryReferenceType.Delivery, ReferenceId = input.DeliveryOrderId.ToString(), ReferenceLineId = input.LineId,
            ReferenceSubKey = "dispatch:" + input.RequestId.ToString("N"), TransactionType = InventoryTransactionType.DeliveryDispatchOut,
            QuantityChange = -proof.Quantity, ForceProvisionalWhenNegative = false };
        return await CreateWithinTransactionAsync(request, warehouse, proof.StoreId, ct, proof, true);
    }
    public async Task<InventoryMovementResultDto> CreateDeliveryReturnAsync(InventoryDeliveryReturnPosting input, CancellationToken ct = default)
    {
        if (!_postingCoordinator.HasActiveTransaction) throw new InvalidOperationException("Delivery receiving requires its caller transaction.");
        var proof = await _sourceEvidence.GetDeliveryReturnProofAsync(input, ct);
        if (proof.DeliveryOrderLineId is not > 0) throw new BusinessRuleException("COST_NOT_READY: thiếu dòng giao gốc đã được xác minh.");
        var warehouse = await _warehouseRepository.GetByIdAsync(proof.WarehouseId, ct) ?? throw new BusinessRuleException("Kho nguồn không tồn tại.");
        var request = new CreateInventoryMovementRequest { WarehouseId = proof.WarehouseId, ProductVariantId = proof.VariantId,
            ReferenceType = InventoryReferenceType.Delivery, ReferenceId = input.DeliveryOrderId.ToString(), ReferenceLineId = proof.DeliveryOrderLineId.Value,
            ReferenceSubKey = "return:" + input.RequestId.ToString("N") + ":" + input.DispatchFragmentId,
            TransactionType = InventoryTransactionType.DeliveryReturnIn, QuantityChange = proof.Quantity, AllowNegativeBalance = true };
        return await CreateWithinTransactionAsync(request, warehouse, proof.StoreId, ct, proof);
    }
    public async Task<bool> TryCreateSourcePreservingTransferAsync(InventorySourcePreservingTransfer input, CancellationToken ct = default)
    {
        if (!_postingCoordinator.HasActiveTransaction) throw new InvalidOperationException("Source transfer requires its caller transaction.");
        var proof = await _sourceEvidence.GetTransferProofAsync(input, ct);
        await PreLockBalancesAsync([new(proof.StoreId, proof.SourceWarehouseId, proof.VariantId), new(proof.StoreId, proof.DestinationWarehouseId, proof.VariantId)], ct);
        var source = await _balanceRepository.LockAndGetOrCreateAsync(proof.StoreId, proof.SourceWarehouseId, proof.VariantId, ct);
        var destination = await _balanceRepository.LockAndGetOrCreateAsync(proof.StoreId, proof.DestinationWarehouseId, proof.VariantId, ct);
        if (source.QuantityPrecisionVersion == 0 && destination.QuantityPrecisionVersion == 0) return false;
        await _sourceEvidence.EnsurePromotedAsync(source, ct); await _sourceEvidence.EnsurePromotedAsync(destination, ct);
        var from = await _warehouseRepository.GetByIdAsync(proof.SourceWarehouseId, ct) ?? throw new BusinessRuleException("Kho nguồn không tồn tại.");
        var to = await _warehouseRepository.GetByIdAsync(proof.DestinationWarehouseId, ct) ?? throw new BusinessRuleException("Kho đích không tồn tại.");
        var outgoing = new CreateInventoryMovementRequest { WarehouseId = from.Id, ProductVariantId = proof.VariantId,
            ReferenceType = InventoryReferenceType.StockTransfer, ReferenceId = proof.DocumentId.ToString(), ReferenceLineId = proof.LineId,
            ReferenceSubKey = "source-budget", TransactionType = InventoryTransactionType.TransferOut, QuantityChange = -proof.Quantity };
        var outResult = await CreateWithinTransactionAsync(outgoing, from, proof.StoreId, ct, null, true);
        var transactionId = outResult.InventoryTransactionId;
        if (transactionId is null)
        {
            var existing = await _transactionRepository.GetByIdempotencyKeyAsync(proof.StoreId,
                InventoryIdempotencyKeyFactory.Create(proof.StoreId, outgoing), ct)
                ?? await _transactionRepository.GetByLegacyIdentityAsync(proof.StoreId, from.Id, proof.VariantId,
                    outgoing.TransactionType, outgoing.ReferenceType, outgoing.ReferenceId!, outgoing.ReferenceLineId,
                    outgoing.ReferenceSubKey, ct)
                ?? throw new BusinessRuleException("COST_NOT_READY: thiếu bút toán xuất nguồn bền vững.");
            await VerifyIdempotentPayloadAsync(existing, proof.StoreId, outgoing, ct);
            transactionId = existing.Id;
        }
        var parts = await _sourceEvidence.GetPostingPartsAsync(proof.StoreId, transactionId.Value, ct);
        var quantity = InventoryPostingPrecisionPolicy.EffectiveDelta(proof.Quantity, false);
        if (parts.Sum(x => x.Quantity) != quantity || parts.Sum(x => x.Amount) != -outResult.ValueChange)
            throw new BusinessRuleException("COST_NOT_READY: mảnh chuyển nguồn không đủ.");
        var incoming = new CreateInventoryMovementRequest { WarehouseId = to.Id, ProductVariantId = proof.VariantId,
            ReferenceType = InventoryReferenceType.StockTransfer, ReferenceId = proof.DocumentId.ToString(), ReferenceLineId = proof.LineId,
            ReferenceSubKey = "source-budget", TransactionType = InventoryTransactionType.TransferIn, QuantityChange = quantity };
        var mirror = new InventoryDeliveryPostingProof(proof.StoreId, to.Id, proof.VariantId, to.LegalEntityId, quantity, null, parts);
        var inResult = await CreateWithinTransactionAsync(incoming, to, proof.StoreId, ct, mirror);
        if (inResult.ValueChange != -outResult.ValueChange) throw new BusinessRuleException("COST_BUDGET_INVALID: chuyển kho không bảo toàn giá trị.");
        return true;
    }

    private async Task<InventoryMovementResultDto> CreatePromotedAsync(CreateInventoryMovementRequest request, Warehouse warehouse,
        InventoryBalance balance, byte[]? identity, decimal protectedQuantity, InventoryDeliveryPostingProof? trusted,
        bool strictFinal, DateTime now, CancellationToken ct)
    {
        var delivery = request.ReferenceType == InventoryReferenceType.Delivery;
        var delta = InventoryPostingPrecisionPolicy.EffectiveDelta(request.QuantityChange, delivery);
        var beforeQty = InventoryPostingPrecisionPolicy.Quantity(balance.OnHandQty);
        var beforeValue = InventoryPostingPrecisionPolicy.Amount(balance.InventoryValue);
        var after = InventoryPostingPrecisionPolicy.Quantity(checked(beforeQty + delta));
        if (delta < 0 && protectedQuantity > 0 && after < protectedQuantity)
            throw new BusinessRuleException("DELIVERY_STOCK_PROTECTED: hàng đang được soạn cho đơn giao.");
        if (after < 0 && !request.AllowNegativeBalance && !warehouse.AllowNegativeInventory)
            throw new BusinessRuleException("INSUFFICIENT_INVENTORY: kho không cho âm tồn.");
        if (strictFinal && after < 0) throw new BusinessRuleException("COST_NOT_READY: xuất nguồn yêu cầu đủ hàng FIFO thực tế.");
        List<InventoryCostLayer>? validatedLayers = null;
        if (delta < 0 && beforeQty > 0)
        {
            validatedLayers = await _costLayerRepository.GetOpenLayersForUpdateAsync(warehouse.Id, request.ProductVariantId, ct);
            decimal sourceQuantity = 0, sourceAmount = 0;
            foreach (var layer in validatedLayers)
            {
                if (layer.StoreId != warehouse.StoreId || layer.WarehouseId != warehouse.Id ||
                    layer.ProductVariantId != request.ProductVariantId || layer.IsProvisionalSource)
                    throw new BusinessRuleException("COST_NOT_READY: nguồn layer không khớp hoặc tạm tính.");
                await _sourceEvidence.PromoteLayerAsync(layer, ct);
                sourceQuantity = checked(sourceQuantity + layer.RemainingQuantity);
                sourceAmount = checked(sourceAmount + layer.RemainingCostAmount!.Value);
            }
            if (sourceQuantity != beforeQty || sourceAmount != beforeValue)
                throw new BusinessRuleException("COST_NOT_READY: lượng/giá trị tồn dương không khớp toàn bộ nguồn FIFO đã xác định; cần đối chiếu riêng.");
        }
        var tx = new InventoryTransaction { StoreId = warehouse.StoreId, WarehouseId = warehouse.Id, ProductVariantId = request.ProductVariantId,
            PostingPrecisionPolicy = delivery ? (byte)2 : (byte)1, IdempotencyKey = identity, TransactionType = request.TransactionType,
            ReferenceType = request.ReferenceType, ReferenceId = request.ReferenceId ?? "", ReferenceLineId = request.ReferenceLineId,
            ReferenceSubKey = request.ReferenceSubKey, QuantityChange = delta, BeforeQty = beforeQty, AfterQty = after,
            BeforeInventoryValue = beforeValue, OccurredAtUtc = now, Note = request.Note };
        await _transactionRepository.AddAsync(tx, ct); await _transactionRepository.SaveChangesAsync(ct);
        var entries = new List<InventoryValuationEntry>();
        var runningQty = beforeQty; var runningValue = beforeValue;
        InventoryValuationEntry Entry(decimal qty, decimal unit, decimal amount, string subkey)
        {
            runningQty = InventoryPostingPrecisionPolicy.Quantity(checked(runningQty + qty));
            runningValue = InventoryPostingPrecisionPolicy.Amount(checked(runningValue + amount));
            return new() { StoreId = warehouse.StoreId, InventoryTransactionId = tx.Id, WarehouseId = warehouse.Id,
                ProductVariantId = request.ProductVariantId, EntryType = qty > 0 ? InventoryValuationEntryType.Inbound : InventoryValuationEntryType.Outbound,
                ReferenceType = request.ReferenceType, ReferenceId = request.ReferenceId ?? "", ReferenceLineId = request.ReferenceLineId,
                ReferenceSubKey = subkey, Quantity = qty, UnitCost = unit, Amount = amount, RunningQtyAfter = runningQty,
                RunningValueAfter = runningValue, RunningAverageUnitCostAfter = runningQty == 0 ? 0 : decimal.Round(runningValue / runningQty, 6, MidpointRounding.AwayFromZero),
                CostSourceType = InventoryCostSourceType.Manual, CostFinalizedAtUtc = now, OccurredAtUtc = now, Note = request.Note };
        }
        if (delta < 0)
        {
            var required = -delta; var actual = Math.Min(required, Math.Max(beforeQty, 0));
            var layers = validatedLayers ?? await _costLayerRepository.GetOpenLayersForUpdateAsync(warehouse.Id, request.ProductVariantId, ct);
            if (layers.Sum(x => x.RemainingQuantity) < actual) throw new BusinessRuleException("COST_NOT_READY: chưa đủ layer FIFO.");
            var remaining = actual;
            foreach (var layer in layers.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id))
            {
                if (remaining == 0) break;
                if (layer.StoreId != warehouse.StoreId || layer.WarehouseId != warehouse.Id || layer.ProductVariantId != request.ProductVariantId || layer.IsProvisionalSource)
                    throw new BusinessRuleException("COST_NOT_READY: nguồn layer không khớp hoặc tạm tính.");
                if (validatedLayers is null) await _sourceEvidence.PromoteLayerAsync(layer, ct);
                var qty = Math.Min(remaining, layer.RemainingQuantity);
                var start = layer.BudgetAnchorQuantity!.Value - layer.RemainingQuantity;
                var amount = InventoryCostBudgetPolicy.Debit(layer, qty);
                var entry = Entry(-qty, layer.UnitCost, -amount, (request.ReferenceSubKey ?? "") + ":L" + layer.Id);
                entry.InventoryCostLayerId = layer.Id;
                await _valuationRepository.AddRangeAsync([entry], ct); await _transactionRepository.SaveChangesAsync(ct);
                var allocation = new InventoryCostLayerAllocation { StoreId = warehouse.StoreId, InventoryValuationEntryId = entry.Id,
                    InventoryCostLayerId = layer.Id, Quantity = qty, UnitCost = layer.UnitCost, Amount = amount, IsResolved = true,
                    ResolvedQuantity = qty, ResolvedAmount = amount, ResolvedAtUtc = now, ResolvedByInventoryCostLayerId = layer.Id };
                await _allocationRepository.AddAsync(allocation, ct); await _allocationRepository.SaveChangesAsync(ct);
                await _sourceEvidence.AddDebitAsync(new() { StoreId = warehouse.StoreId, WarehouseId = warehouse.Id,
                    ProductVariantId = request.ProductVariantId, SourceLegalEntityId = warehouse.LegalEntityId,
                    InventoryCostLayerId = layer.Id, InventoryTransactionId = tx.Id, InventoryValuationEntryId = entry.Id,
                    InventoryCostLayerAllocationId = allocation.Id, AnchorQuantity = layer.BudgetAnchorQuantity.Value,
                    AnchorAmount = layer.BudgetAnchorAmount!.Value, StartQuantity = start, EndQuantity = start + qty,
                    UnitCost = layer.UnitCost, Amount = amount }, ct);
                await _sourceEvidence.SaveChangesAsync(ct);
                entries.Add(entry); remaining -= qty;
            }
            var provisional = required - actual;
            if (provisional > 0)
            {
                if (strictFinal) throw new BusinessRuleException("COST_NOT_READY: thiếu nguồn đã xác định.");
                if (!request.ForceProvisionalWhenNegative) throw new BusinessRuleException("COST_NOT_READY: yêu cầu không cho ghi giá vốn tạm tính.");
                var unit = InventoryPostingPrecisionPolicy.Cost(request.ProvisionalUnitCost is > 0 ? request.ProvisionalUnitCost.Value :
                    balance.LastInboundUnitCost is > 0 ? balance.LastInboundUnitCost.Value : balance.AverageUnitCost > 0 ? balance.AverageUnitCost : request.UnitCost is > 0 ? request.UnitCost.Value : 0);
                var entry = Entry(-provisional, unit, -decimal.Round(provisional * unit, 4, MidpointRounding.AwayFromZero), (request.ReferenceSubKey ?? "") + ":PROV");
                entry.CostSourceType = unit == 0 ? InventoryCostSourceType.Unknown : request.ProvisionalUnitCost is > 0 ? InventoryCostSourceType.Manual :
                    balance.LastInboundUnitCost is > 0 ? InventoryCostSourceType.LastInboundCost : InventoryCostSourceType.MovingAverage;
                entry.IsProvisional = true; entry.CostFinalizedAtUtc = null;
                await _valuationRepository.AddRangeAsync([entry], ct); await _transactionRepository.SaveChangesAsync(ct);
                await _allocationRepository.AddAsync(new() { StoreId = warehouse.StoreId, InventoryValuationEntryId = entry.Id,
                    Quantity = provisional, UnitCost = unit, Amount = -entry.Amount, IsProvisional = true }, ct);
                await _allocationRepository.SaveChangesAsync(ct); entries.Add(entry);
            }
        }
        else
        {
            var parts = trusted?.Parts;
            if (parts is null || parts.Count == 0)
            {
                if (request.UnitCost is not > 0) throw new BusinessRuleException("Movement nhập phải có UnitCost hợp lệ.");
                var unit = InventoryPostingPrecisionPolicy.Cost(request.UnitCost.Value);
                parts = [new(0, null, 0, delta, unit, decimal.Round(delta * unit, 4, MidpointRounding.AwayFromZero))];
            }
            if (parts.Sum(x => x.Quantity) != delta) throw new BusinessRuleException("COST_BUDGET_INVALID: lượng nguồn nhập không khớp.");
            var partNo = 0;
            foreach (var part in parts)
            {
                InventoryPostingPrecisionPolicy.Cost(part.UnitCost); InventoryPostingPrecisionPolicy.Amount(part.Amount);
                var entry = Entry(part.Quantity, part.UnitCost, part.Amount, (request.ReferenceSubKey ?? "") + ":PART:" + ++partNo);
                entry.SourceValuationEntryId = part.SourceValuationEntryId > 0 ? part.SourceValuationEntryId : null;
                entry.SourceReferenceSubKey = part.SourceReferenceSubKey;
                await _valuationRepository.AddRangeAsync([entry], ct); await _transactionRepository.SaveChangesAsync(ct);
                var layer = new InventoryCostLayer { StoreId = warehouse.StoreId, WarehouseId = warehouse.Id, ProductVariantId = request.ProductVariantId,
                    InventoryTransactionId = tx.Id, InventoryValuationEntryId = entry.Id, ReferenceType = request.ReferenceType,
                    ReferenceId = request.ReferenceId ?? "", ReferenceLineId = request.ReferenceLineId, ReferenceSubKey = entry.ReferenceSubKey,
                    OriginalQuantity = part.Quantity, RemainingQuantity = part.Quantity, RemainingOpenProvisionalQty = part.Quantity, UnitCost = part.UnitCost,
                    CostBudgetPolicy = 1, BudgetAnchorQuantity = part.Quantity, BudgetAnchorAmount = part.Amount,
                    RemainingCostAmount = part.Amount, OccurredAtUtc = now };
                await _costLayerRepository.AddAsync(layer, ct); await _costLayerRepository.SaveChangesAsync(ct);
                entry.InventoryCostLayerId = layer.Id;
                runningValue += await ResolvePromotedProvisionalsAsync(layer, warehouse, now, runningQty, runningValue, ct);
                entries.Add(entry);
            }
        }
        if (runningQty != after || entries.Sum(x => x.Quantity) != delta) throw new BusinessRuleException("INVENTORY_PRECISION: tổng mảnh không khớp bút toán.");
        // Resolution records are value-only; their adjustment is included in the current balance below.
        balance.OnHandQty = after;
        balance.InventoryValue = InventoryPostingPrecisionPolicy.Amount(runningValue);
        balance.AverageUnitCost = after == 0 ? 0 : decimal.Round(balance.InventoryValue / after, 6, MidpointRounding.AwayFromZero);
        balance.LastValuationAtUtc = now;
        if (delta > 0) { balance.LastInboundUnitCost = entries[^1].UnitCost; balance.LastInboundAtUtc = now; }
        tx.TotalCost = entries.Sum(x => x.Amount); tx.AfterInventoryValue = balance.InventoryValue;
        tx.UnitCostSnapshot = entries.Select(x => x.UnitCost).Distinct().Count() == 1 ? entries[0].UnitCost : 0;
        tx.CostSourceType = entries[^1].CostSourceType;
        tx.RunningAverageUnitCostAfter = balance.AverageUnitCost; tx.IsProvisionalCost = entries.Any(x => x.IsProvisional);
        tx.CostFinalizedAtUtc = tx.IsProvisionalCost ? null : now;
        await _balanceRepository.SaveChangesAsync(ct);
        return new() { WarehouseId = warehouse.Id, ProductVariantId = request.ProductVariantId, BeforeQty = beforeQty, QuantityChange = delta,
            AfterQty = after, BeforeValue = beforeValue, ValueChange = tx.TotalCost, AfterValue = balance.InventoryValue,
            BeforeAverageCost = beforeQty == 0 ? 0 : decimal.Round(beforeValue / beforeQty, 6, MidpointRounding.AwayFromZero),
            AfterAverageCost = balance.AverageUnitCost, InventoryTransactionId = tx.Id, InventoryValuationEntryIds = entries.Select(x => x.Id).ToList(),
            IsCreated = true, IsNegativeAfterTransaction = after < 0, HasProvisionalValuation = tx.IsProvisionalCost,
            ProvisionalQuantity = entries.Where(x => x.IsProvisional).Sum(x => -x.Quantity),
            ProvisionalValueChange = entries.Where(x => x.IsProvisional).Sum(x => x.Amount),
            ActualQuantity = entries.Where(x => !x.IsProvisional).Sum(x => Math.Abs(x.Quantity)),
            ActualValueChange = entries.Where(x => !x.IsProvisional).Sum(x => x.Amount) };
    }
    public async Task<List<ProvisionalRevaluationPlanDto>> ResolveSourceBudgetProvisionalsAsync(int layerId, DateTime occurredAtUtc, CancellationToken ct = default)
    {
        if (!_postingCoordinator.HasActiveTransaction) throw new BusinessRuleException("TRANSACTION_REQUIRED: giải quyết ngân sách cần giao dịch đang mở.");
        var original = await _costLayerRepository.GetByIdAsync(layerId, ct) ?? throw new BusinessRuleException("COST_NOT_READY: nguồn nhập không tồn tại.");
        var warehouse = await _warehouseRepository.GetByIdAsync(original.WarehouseId, ct) ?? throw new BusinessRuleException("COST_NOT_READY: kho nhập không tồn tại.");
        if (original.StoreId != warehouse.StoreId) throw new BusinessRuleException("COST_NOT_READY: nguồn nhập khác cửa hàng.");
        var balance = await _balanceRepository.LockAndGetOrCreateAsync(warehouse.StoreId, warehouse.Id, original.ProductVariantId, ct);
        await _sourceEvidence.EnsurePromotedAsync(balance, ct);
        var layer = (await _costLayerRepository.GetOpenLayersForUpdateAsync(warehouse.Id, original.ProductVariantId, ct)).SingleOrDefault(x => x.Id == layerId);
        if (layer is null) return [];
        await _sourceEvidence.PromoteLayerAsync(layer, ct);
        var allocations = await _allocationRepository.GetOpenProvisionalAllocationsForUpdateAsync(warehouse.Id, layer.ProductVariantId, ct);
        var before = allocations.ToDictionary(x => x.Id, x => (x.ResolvedQuantity, x.ResolvedAmount));
        var adjustment = await ResolvePromotedProvisionalsAsync(layer, warehouse, occurredAtUtc, balance.OnHandQty, balance.InventoryValue, ct);
        balance.InventoryValue = InventoryPostingPrecisionPolicy.Amount(balance.InventoryValue + adjustment);
        balance.AverageUnitCost = balance.OnHandQty == 0 ? 0 : decimal.Round(balance.InventoryValue / balance.OnHandQty, 6, MidpointRounding.AwayFromZero);
        balance.LastValuationAtUtc = occurredAtUtc;
        await _balanceRepository.SaveChangesAsync(ct);
        return allocations.Where(x => x.ResolvedQuantity > before[x.Id].ResolvedQuantity).Select(x => new ProvisionalRevaluationPlanDto
        {
            ProvisionalEntryId = x.InventoryValuationEntryId, ProvisionalAllocationId = x.Id, InboundLayerId = layer.Id,
            QuantityAbs = x.ResolvedQuantity - before[x.Id].ResolvedQuantity, ProvisionalUnitCost = x.UnitCost, FinalUnitCost = layer.UnitCost,
            UnitCostDelta = layer.UnitCost - x.UnitCost,
            RevaluationAmount = InventoryCostBudgetPolicy.Slice(x.Quantity, x.Amount, before[x.Id].ResolvedQuantity, x.ResolvedQuantity - before[x.Id].ResolvedQuantity)
                - (x.ResolvedAmount - before[x.Id].ResolvedAmount)
        }).ToList();
    }
    private async Task<decimal> ResolvePromotedProvisionalsAsync(InventoryCostLayer layer, Warehouse warehouse, DateTime now,
        decimal quantityAfter, decimal valueAfterInbound, CancellationToken ct)
    {
        decimal totalAdjustment = 0;
        var allocations = await _allocationRepository.GetOpenProvisionalAllocationsForUpdateAsync(layer.WarehouseId, layer.ProductVariantId, ct);
        foreach (var allocation in allocations)
        {
            if (layer.RemainingQuantity == 0) break;
            if (allocation.StoreId != layer.StoreId || allocation.Quantity < allocation.ResolvedQuantity) throw new BusinessRuleException("COST_NOT_READY: nguồn tạm tính không khớp.");
            var open = allocation.Quantity - allocation.ResolvedQuantity;
            if (open == 0) continue;
            var qty = Math.Min(open, layer.RemainingQuantity);
            var start = layer.BudgetAnchorQuantity!.Value - layer.RemainingQuantity;
            var actual = InventoryCostBudgetPolicy.Debit(layer, qty);
            var provisional = InventoryCostBudgetPolicy.Slice(allocation.Quantity, allocation.Amount, allocation.ResolvedQuantity, qty);
            var source = allocation.InventoryValuationEntry;
            if (source.Quantity != -allocation.Quantity || source.Amount != -allocation.Amount || source.StoreId != layer.StoreId ||
                source.WarehouseId != layer.WarehouseId || source.ProductVariantId != layer.ProductVariantId || source.EntryType != InventoryValuationEntryType.Outbound)
                throw new BusinessRuleException("COST_NOT_READY: nguồn tạm tính đã thay đổi/không còn đủ chứng cứ.");
            var difference = provisional - actual;
            InventoryValuationEntry? revaluation = null;
            var identityRequest = new CreateInventoryMovementRequest { WarehouseId = layer.WarehouseId, ProductVariantId = layer.ProductVariantId,
                TransactionType = InventoryTransactionType.Revaluation, ReferenceType = source.ReferenceType, ReferenceId = source.ReferenceId,
                ReferenceLineId = source.ReferenceLineId, ReferenceSubKey = $"BUDGET:L{layer.Id}:A{allocation.Id}:S{allocation.ResolvedQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
            var resolutionTx = new InventoryTransaction { StoreId = layer.StoreId, WarehouseId = layer.WarehouseId, ProductVariantId = layer.ProductVariantId,
                TransactionType = InventoryTransactionType.Revaluation, PostingPrecisionPolicy = 1,
                IdempotencyKey = InventoryIdempotencyKeyFactory.Create(layer.StoreId, identityRequest),
                ReferenceType = source.ReferenceType, ReferenceId = source.ReferenceId, ReferenceLineId = source.ReferenceLineId,
                ReferenceSubKey = identityRequest.ReferenceSubKey, QuantityChange = 0, BeforeQty = quantityAfter, AfterQty = quantityAfter,
                BeforeInventoryValue = valueAfterInbound + totalAdjustment, AfterInventoryValue = valueAfterInbound + totalAdjustment + difference,
                TotalCost = difference, UnitCostSnapshot = layer.UnitCost, OccurredAtUtc = now, CostFinalizedAtUtc = now };
            await _transactionRepository.AddAsync(resolutionTx, ct); await _transactionRepository.SaveChangesAsync(ct);
            if (difference != 0)
            {
                revaluation = new() { StoreId = layer.StoreId, InventoryTransactionId = resolutionTx.Id,
                    WarehouseId = layer.WarehouseId, ProductVariantId = layer.ProductVariantId, EntryType = InventoryValuationEntryType.Revaluation,
                    ReferenceType = source.ReferenceType, ReferenceId = source.ReferenceId, ReferenceLineId = source.ReferenceLineId,
                    ReferenceSubKey = source.ReferenceSubKey, SourceValuationEntryId = source.Id, SourceReferenceSubKey = source.ReferenceSubKey,
                    RevaluationOfEntryId = source.Id, InventoryCostLayerId = layer.Id, Quantity = 0, UnitCost = layer.UnitCost,
                    Amount = difference, CostSourceType = InventoryCostSourceType.RevaluationAdjustment, CostFinalizedAtUtc = now, OccurredAtUtc = now,
                    RunningQtyAfter = quantityAfter, RunningValueAfter = resolutionTx.AfterInventoryValue,
                    RunningAverageUnitCostAfter = quantityAfter == 0 ? 0 : decimal.Round(resolutionTx.AfterInventoryValue / quantityAfter, 6, MidpointRounding.AwayFromZero) };
                await _valuationRepository.AddRangeAsync([revaluation], ct); await _transactionRepository.SaveChangesAsync(ct);
            }
            var debit = new InventoryCostBudgetDebit { StoreId = layer.StoreId, WarehouseId = layer.WarehouseId, ProductVariantId = layer.ProductVariantId,
                SourceLegalEntityId = warehouse.LegalEntityId, InventoryCostLayerId = layer.Id, InventoryTransactionId = resolutionTx.Id,
                InventoryValuationEntryId = source.Id, InventoryCostLayerAllocationId = allocation.Id, AnchorQuantity = layer.BudgetAnchorQuantity.Value,
                AnchorAmount = layer.BudgetAnchorAmount!.Value, StartQuantity = start, EndQuantity = start + qty, UnitCost = layer.UnitCost,
                Amount = actual, IsProvisionalResolution = true, ResolutionStartQuantity = allocation.ResolvedQuantity,
                ResolutionEndQuantity = allocation.ResolvedQuantity + qty, RevaluationEntryId = revaluation?.Id };
            allocation.ResolvedQuantity += qty; allocation.ResolvedAmount += actual; allocation.IsResolved = allocation.ResolvedQuantity == allocation.Quantity;
            allocation.ResolvedAtUtc = allocation.IsResolved ? now : null; allocation.ResolvedByInventoryCostLayerId = layer.Id;
            source.CostFinalizedAtUtc = allocation.IsResolved ? now : null; layer.ResolvedProvisionalQty += qty;
            layer.RemainingOpenProvisionalQty = layer.RemainingQuantity;
            totalAdjustment += difference;
            // Persist resolved facts first; the immutable debit trigger observes their final state.
            await _allocationRepository.SaveChangesAsync(ct);
            await _sourceEvidence.AddDebitAsync(debit, ct);
            await _sourceEvidence.SaveChangesAsync(ct);
        }
        return totalAdjustment;
    }

    private static bool InboundLikeLastEntryIsProvisional(List<InventoryValuationEntry> entries)
    {
        var inboundEntry = entries.FirstOrDefault(x => x.EntryType == InventoryValuationEntryType.Inbound);
        return inboundEntry != null && inboundEntry.IsProvisional;
    }
}

using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

/// <summary>
/// Adapter có side effect của Phase 22.5. Service này chỉ được gọi bên trong
/// transaction finalize do POSService sở hữu.
/// </summary>
public sealed class OrderLegalEntityFinalizeService : IOrderLegalEntityFinalizeService
{
    private const decimal MoneyTolerance = 0.01m;
    private const decimal QuantityTolerance = 0.0001m;

    private readonly IOrderLegalEntityAllocationRepository _repository;
    private readonly IOrderLegalEntityAllocationService _allocationEngine;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;

    public OrderLegalEntityFinalizeService(
        IOrderLegalEntityAllocationRepository repository,
        IOrderLegalEntityAllocationService allocationEngine,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory)
    {
        _repository = repository;
        _allocationEngine = allocationEngine;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
    }

    public async Task CaptureModeAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.StoreId <= 0)
            throw new InvalidOperationException("Order does not have a valid StoreId for LegalEntity mode capture.");

        // Keep the original cohort when a create/save operation is retried.
        if (order.LegalEntityModeCapturedAtUtc.HasValue)
            return;

        var store = await _repository.GetStoreFeatureStateAsync(order.StoreId, ct)
            ?? throw ConfigurationError("The order Store could not be found.");
        var now = DateTime.UtcNow;
        var useMultiLegalEntity = store.IsMultiLegalEntityEnabled &&
                                  store.MultiLegalEntityActivatedAtUtc.HasValue &&
                                  store.MultiLegalEntityActivatedAtUtc.Value <= now;

        order.UseMultiLegalEntity = useMultiLegalEntity;
        order.LegalEntityModeCapturedAtUtc = now;
        order.LegalEntityActivationAtUtcSnapshot = useMultiLegalEntity
            ? store.MultiLegalEntityActivatedAtUtc
            : null;
    }

    public async Task<OrderLegalEntityFinalizeResult> ApplyIfEnabledAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Id <= 0 || order.StoreId <= 0)
            throw new InvalidOperationException("Order chưa có Id/StoreId hợp lệ để allocation.");

        var store = await _repository.GetStoreFeatureStateAsync(order.StoreId, ct)
            ?? throw ConfigurationError("Không tìm thấy Store của đơn hàng.");

        var now = DateTime.UtcNow;
        if (!ShouldUseMultiLegalEntity(order, store, now))
            return OrderLegalEntityFinalizeResult.FeatureDisabled;

        var activationAt = ResolveActivationAt(order, store);
        if (!activationAt.HasValue || activationAt.Value > now)
        {
            throw ConfigurationError(
                "Cửa hàng đã bật Multi LegalEntity nhưng chưa có ngày kích hoạt hợp lệ.");
        }

        if (await _repository.AnyForOrderAsync(order.Id, ct))
        {
            throw PosAppException.StateConflict(
                PosErrorCodes.CheckoutLegalEntityAllocationAlreadyExists,
                "Đơn đã có dữ liệu phân bổ HKD và không thể chốt lặp lại.",
                "Vui lòng tải lại trạng thái đơn trước khi thao tác tiếp.",
                new { order.Id, order.OrderNumber });
        }

        var lines = order.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        ValidateOrderLines(lines);

        var legalEntities = await _repository.GetActiveSalesLegalEntitiesAsync(order.StoreId, ct);
        var sources = BuildAndValidateSources(order.StoreId, legalEntities);
        var warehouseIds = sources.Select(x => x.WarehouseId).ToList();
        var variantIds = lines.Select(x => x.VariantId).Distinct().ToList();

        // Repository dùng UPDLOCK + HOLDLOCK trên SQL Server. Snapshot sau lệnh
        // này được giữ ổn định cho tới khi transaction finalize commit/rollback.
        var lockedBalances = await _repository.LockInventoryForAllocationAsync(
            order.StoreId,
            warehouseIds,
            variantIds,
            ct);

        var request = new OrderLegalEntityAllocationRequest
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            AllowNegativeInventory = true,
            Lines = lines.Select(x => new OrderLegalEntityAllocationLineInput
            {
                OrderLineId = x.Id,
                ProductVariantId = x.VariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                Quantity = x.Quantity,
                Multiplier = x.Multiplier,
                BaseQuantity = x.BaseQuantity
            }).ToList(),
            EligibleSources = sources,
            Inventory = lockedBalances.Select(x => new LegalEntityInventoryAvailabilityInput
            {
                WarehouseId = x.WarehouseId,
                ProductVariantId = x.ProductVariantId,
                OnHandBaseQuantity = x.OnHandQty,
                ReservedBaseQuantity = x.ReservedQty
            }).ToList()
        };

        var preview = _allocationEngine.Preview(request);
        if (!preview.IsSuccess)
        {
            var lineById = lines.ToDictionary(x => x.Id);
            var shortages = preview.Shortages.Select(x =>
            {
                lineById.TryGetValue(x.OrderLineId, out var line);

                return new
                {
                    x.OrderLineId,
                    x.ProductVariantId,
                    ItemName = string.IsNullOrWhiteSpace(line?.ItemName)
                        ? $"Variant #{x.ProductVariantId}"
                        : line.ItemName.Trim(),
                    Sku = line?.Sku,
                    BaseUnitName = line?.BaseUnitName,
                    x.RequiredBaseQuantity,
                    x.AllocatedBaseQuantity,
                    x.ShortageBaseQuantity
                };
            }).ToList();

            var shortageSummary = string.Join(
                "; ",
                shortages.Take(4).Select(x =>
                    $"{x.ItemName} thiếu {x.ShortageBaseQuantity:0.####}" +
                    (string.IsNullOrWhiteSpace(x.BaseUnitName) ? string.Empty : $" {x.BaseUnitName}")));

            if (shortages.Count > 4)
                shortageSummary += $"; và {shortages.Count - 4} sản phẩm khác";

            throw PosAppException.StateConflict(
                PosErrorCodes.CheckoutLegalEntityInsufficientInventory,
                $"Không đủ tồn khả dụng để hoàn tất đơn hàng: {shortageSummary}.",
                "Vui lòng bổ sung/chuyển tồn hoặc giảm số lượng sản phẩm rồi thử lại.",
                new
                {
                    order.Id,
                    preview.TotalRequiredBaseQuantity,
                    preview.TotalAllocatedBaseQuantity,
                    preview.TotalShortageBaseQuantity,
                    Shortages = shortages
                });
        }

        var persistedAllocations = BuildPersistedAllocations(order, lines, preview);
        var balanceKeys = preview.Allocations
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.WarehouseId,
                x.ProductVariantId))
            .Distinct()
            .ToList();
        if (balanceKeys.Count > 0)
        {
            await _inventoryMovementService.PreLockBalancesAsync(
                balanceKeys,
                ct);
        }

        var inventoryResult = await ApplyInventoryMovementsAsync(
            order,
            lines,
            preview,
            persistedAllocations,
            now,
            ct);

        // Chỉ attach allocation sau khi toàn bộ movement đã thành công. SaveChanges
        // tiếp theo trong finalize vẫn nằm trong cùng transaction ngoài cùng.
        await _repository.AddRangeAsync(persistedAllocations, ct);

        order.LegalEntityCount = preview.LegalEntityCount;
        order.HasMultipleLegalEntities = preview.HasMultipleLegalEntities;
        order.LegalEntityAllocatedAtUtc = now;

        return new OrderLegalEntityFinalizeResult
        {
            IsFeatureEnabled = true,
            WasApplied = true,
            LegalEntityCount = preview.LegalEntityCount,
            AllocationCount = persistedAllocations.Count,
            HasNegativeInventory = inventoryResult.Any(x => x.IsNegativeInventory),
            HasProvisionalCost = inventoryResult.Any(x => x.IsProvisionalCost),
            IssueLines = inventoryResult
                .Where(x => x.IsNegativeInventory || x.IsProvisionalCost)
                .ToList()
        };
    }

    public Task<bool> HasPersistedAllocationAsync(
        int orderId,
        CancellationToken ct = default)
        => orderId <= 0
            ? Task.FromResult(false)
            : _repository.AnyForOrderAsync(orderId, ct);

    private static bool ShouldUseMultiLegalEntity(Order order, Store store, DateTime now)
    {
        var requested = order.LegalEntityModeCapturedAtUtc.HasValue
            ? order.UseMultiLegalEntity
            : store.IsMultiLegalEntityEnabled;
        if (!requested)
            return false;

        var activationAt = ResolveActivationAt(order, store);
        if (!activationAt.HasValue || activationAt.Value > now)
        {
            throw ConfigurationError(
                "Multi LegalEntity mode does not have a valid activation timestamp.");
        }

        // Compatibility for rows created before Phase 22.9: a cart older than the
        // current activation remains legacy. Default CreatedAtUtc keeps unit-test
        // fixtures and non-persisted callers on the previous behavior.
        return order.LegalEntityModeCapturedAtUtc.HasValue ||
               order.CreatedAtUtc == default ||
               order.CreatedAtUtc >= activationAt.Value;
    }

    private static DateTime? ResolveActivationAt(Order order, Store store)
        => order.LegalEntityModeCapturedAtUtc.HasValue && order.UseMultiLegalEntity
            ? order.LegalEntityActivationAtUtcSnapshot
            : store.MultiLegalEntityActivatedAtUtc;

    private static void ValidateOrderLines(IReadOnlyCollection<OrderLine> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("Đơn phải có ít nhất một dòng để allocation.");

        foreach (var line in lines)
        {
            if (line.Id <= 0 || line.VariantId <= 0)
                throw new InvalidOperationException("OrderLine/Variant chưa có Id hợp lệ.");

            if (line.Quantity <= 0 || line.Multiplier <= 0 || line.BaseQuantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Dòng #{line.Id} thiếu Quantity/Multiplier/BaseQuantity hợp lệ.");
            }

            if (Math.Abs((line.Quantity * line.Multiplier) - line.BaseQuantity) > QuantityTolerance)
            {
                throw new InvalidOperationException(
                    $"Dòng #{line.Id} có BaseQuantity không bằng Quantity × Multiplier.");
            }
        }
    }

    private static List<LegalEntityAllocationSourceInput> BuildAndValidateSources(
        int storeId,
        IReadOnlyCollection<LegalEntity> legalEntities)
    {
        if (legalEntities.Count < 2)
        {
            throw ConfigurationError(
                "Multi LegalEntity yêu cầu ít nhất hai HKD đang hoạt động.");
        }

        var result = new List<LegalEntityAllocationSourceInput>();

        foreach (var legalEntity in legalEntities.OrderBy(x => x.SalePriority).ThenBy(x => x.Id))
        {
            var warehouse = legalEntity.DefaultWarehouse;
            if (legalEntity.StoreId != storeId || legalEntity.SalePriority <= 0)
                throw ConfigurationError($"HKD '{legalEntity.Name}' có Store/ưu tiên bán không hợp lệ.");

            if (!legalEntity.DefaultWarehouseId.HasValue || warehouse == null)
                throw ConfigurationError($"HKD '{legalEntity.Name}' chưa có kho bán mặc định.");

            if (warehouse.IsDeleted || !warehouse.IsActive)
                throw ConfigurationError($"Kho mặc định của HKD '{legalEntity.Name}' đã ngừng hoạt động.");

            if (warehouse.StoreId != storeId || warehouse.LegalEntityId != legalEntity.Id)
                throw ConfigurationError($"Kho mặc định của HKD '{legalEntity.Name}' sai ownership.");

            result.Add(new LegalEntityAllocationSourceInput
            {
                LegalEntityId = legalEntity.Id,
                WarehouseId = warehouse.Id,
                SalePriority = legalEntity.SalePriority
            });
        }

        if (result.GroupBy(x => x.SalePriority).Any(x => x.Count() > 1))
            throw ConfigurationError("Các HKD đang bị trùng thứ tự ưu tiên bán.");

        if (result.GroupBy(x => x.WarehouseId).Any(x => x.Count() > 1))
            throw ConfigurationError("Một kho mặc định đang được gán cho nhiều HKD.");

        return result;
    }

    private static List<OrderLegalEntityAllocation> BuildPersistedAllocations(
        Order order,
        IReadOnlyCollection<OrderLine> lines,
        OrderLegalEntityAllocationResult preview)
    {
        var lineById = lines.ToDictionary(x => x.Id);
        var rows = new List<OrderLegalEntityAllocation>();

        foreach (var lineGroup in preview.Allocations
                     .GroupBy(x => x.OrderLineId)
                     .OrderBy(x => x.Key))
        {
            var line = lineById[lineGroup.Key];
            var items = lineGroup
                .OrderBy(x => x.SalePriority)
                .ThenBy(x => x.LegalEntityId)
                .ToList();
            var weights = items.Select(x => x.BaseQuantity).ToList();
            var lineTotals = DistributeMoney(line.LineTotal, weights);
            var lineDiscounts = DistributeMoney(line.LineDiscount, weights);
            var promotionDiscounts = DistributeMoney(line.PromotionDiscount, weights);

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                rows.Add(new OrderLegalEntityAllocation
                {
                    StoreId = order.StoreId,
                    OrderId = order.Id,
                    OrderLineId = line.Id,
                    ProductVariantId = line.VariantId,
                    ProductUnitConversionId = line.ProductUnitConversionId,
                    LegalEntityId = item.LegalEntityId,
                    WarehouseId = item.WarehouseId,
                    SalePriority = item.SalePriority,
                    Quantity = item.Quantity,
                    BaseQuantity = item.BaseQuantity,
                    UnitPrice = line.UnitPrice,
                    LineTotal = lineTotals[index],
                    DiscountAllocated = lineDiscounts[index],
                    PromotionDiscountAllocated = promotionDiscounts[index],
                    NetAmount = lineTotals[index],
                    AllocationSource = item.AllocationSource,
                    Note = item.AllocationSource == OrderLegalEntityAllocationSource.AutoNegativeFallback
                        ? $"Phase 22.5 auto allocation; phần thiếu được phép âm tại SalePriority={item.SalePriority}."
                        : $"Phase 22.5 auto allocation theo SalePriority={item.SalePriority}."
                });
            }
        }

        ApplyOrderLevelDiscount(
            rows,
            order.ComboDiscountTotal,
            (row, value) => row.ComboDiscountAllocated = value);
        ApplyOrderLevelDiscount(
            rows,
            order.OrderDiscount,
            (row, value) => row.OrderDiscountAllocated = value);
        ApplyOrderLevelDiscount(
            rows,
            order.VoucherDiscountTotal,
            (row, value) => row.VoucherDiscountAllocated = value);

        var allocatedNet = RoundMoney(rows.Sum(x => x.NetAmount));
        var expectedNet = RoundMoney(order.GrandTotal);
        if (Math.Abs(allocatedNet - expectedNet) > MoneyTolerance)
        {
            throw new InvalidOperationException(
                $"Tổng NetAmount allocation ({allocatedNet}) khác GrandTotal ({expectedNet}).");
        }

        return rows;
    }

    private async Task<List<OrderLegalEntityFinalizeInventoryLine>> ApplyInventoryMovementsAsync(
        Order order,
        IReadOnlyCollection<OrderLine> lines,
        OrderLegalEntityAllocationResult preview,
        IReadOnlyCollection<OrderLegalEntityAllocation> persistedAllocations,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var persistedByKey = persistedAllocations.ToDictionary(
            x => (x.OrderLineId, x.LegalEntityId, x.WarehouseId));
        var result = new List<OrderLegalEntityFinalizeInventoryLine>();

        foreach (var line in lines.OrderBy(x => x.Id))
        {
            var fragments = preview.Allocations
                .Where(x => x.OrderLineId == line.Id)
                .OrderBy(x => x.SalePriority)
                .ThenBy(x => x.LegalEntityId)
                .ToList();

            var provisionalFallback = ResolveProvisionalUnitCost(line);
            decimal totalCost = 0m;
            decimal totalBaseQuantity = 0m;
            decimal totalBeforeQty = 0m;
            decimal totalAfterQty = 0m;
            decimal provisionalQuantity = 0m;
            decimal provisionalCostAmount = 0m;
            var hasProvisional = false;
            var hasNegative = false;
            decimal negativeQuantityCreated = 0m;
            var notes = new List<string>();

            foreach (var fragment in fragments)
            {
                var request = _inventoryMovementFactory.CreateSaleFinalize(
                    fragment.WarehouseId,
                    line.VariantId,
                    order.Id,
                    line.Id,
                    line.ItemName,
                    fragment.BaseQuantity,
                    provisionalFallback,
                    occurredAtUtc);

                request.ReferenceSubKey =
                    $"LE:{fragment.LegalEntityId}:WH:{fragment.WarehouseId}";
                request.Note =
                    $"{request.Note}; LegalEntityId={fragment.LegalEntityId}; " +
                    $"AllocationBaseQty={fragment.BaseQuantity:n4}";
                // Đồng nhất với POS legacy: bán vẫn hoàn tất khi thiếu tồn.
                // Inventory issue phía sau chịu trách nhiệm hậu kiểm phần âm.
                request.AllowNegativeBalance = true;

                var movement = await _inventoryMovementService.CreateAsync(request, ct);
                if (movement.IsSkipped || !movement.IsCreated || !movement.InventoryTransactionId.HasValue)
                {
                    throw PosAppException.StateConflict(
                        PosErrorCodes.CheckoutLegalEntityMovementConflict,
                        "Không thể ghi xuất kho cho phân bổ HKD của đơn hàng.",
                        "Vui lòng tải lại đơn và kiểm tra lịch sử xuất kho trước khi thử lại.",
                        new
                        {
                            OrderId = order.Id,
                            OrderLineId = line.Id,
                            fragment.LegalEntityId,
                            fragment.WarehouseId,
                            movement.IsSkipped,
                            movement.IsCreated
                        });
                }

                hasNegative |= movement.IsNegativeAfterTransaction;
                negativeQuantityCreated += Math.Max(0m, -movement.AfterQty) -
                                           Math.Max(0m, -movement.BeforeQty);

                var persisted = persistedByKey[
                    (line.Id, fragment.LegalEntityId, fragment.WarehouseId)];
                persisted.InventoryTransactionId = movement.InventoryTransactionId;

                totalBaseQuantity += fragment.BaseQuantity;
                totalCost += Math.Abs(movement.ValueChange);
                totalBeforeQty += movement.BeforeQty;
                totalAfterQty += movement.AfterQty;
                hasProvisional |= movement.HasProvisionalValuation;

                if (movement.HasProvisionalValuation && movement.ProvisionalQuantity > 0)
                {
                    provisionalQuantity += movement.ProvisionalQuantity;
                    provisionalCostAmount +=
                        movement.ProvisionalQuantity * movement.ProvisionalUnitCost;
                }

                notes.Add(
                    $"LE={fragment.LegalEntityId}; WH={fragment.WarehouseId}; " +
                    $"Tx={movement.InventoryTransactionId}; Qty={fragment.BaseQuantity:n4}; " +
                    $"Before={movement.BeforeQty:n4}; After={movement.AfterQty:n4}");
            }

            line.LineCostTotal = RoundCostAmount(totalCost);
            line.UnitCostSnapshot = totalBaseQuantity > 0
                ? RoundUnitCost(totalCost / totalBaseQuantity)
                : 0m;
            line.IsProvisionalCost = hasProvisional;
            line.ProvisionalUnitCost = hasProvisional && provisionalQuantity > 0
                ? RoundUnitCost(provisionalCostAmount / provisionalQuantity)
                : null;
            line.CostSnapshotNote = TrimToLength(
                "Finalize POS Multi LegalEntity. " + string.Join(" | ", notes),
                1000);
            line.GrossProfit = line.LineTotal - line.LineCostTotal;

            result.Add(new OrderLegalEntityFinalizeInventoryLine
            {
                OrderLineId = line.Id,
                ProductId = line.ProductId,
                VariantId = line.VariantId,
                ProductUnitConversionId = line.ProductUnitConversionId,
                ItemName = line.ItemName,
                BaseQuantity = totalBaseQuantity,
                IsNegativeInventory = hasNegative,
                // OrderInventoryIssue lưu theo OrderLine, còn movement có thể split nhiều kho.
                // Snapshot tổng hợp này làm NegativeQty bằng đúng phần âm mới phát sinh;
                // chi tiết WH/LE/Tx vẫn được giữ trong Note và allocation.
                BeforeQty = hasNegative ? 0m : totalBeforeQty,
                AfterQty = hasNegative ? -Math.Max(0m, negativeQuantityCreated) : totalAfterQty,
                IsProvisionalCost = hasProvisional,
                UnitCostSnapshot = line.UnitCostSnapshot,
                ProvisionalUnitCost = line.ProvisionalUnitCost,
                Note = line.CostSnapshotNote
            });
        }

        return result;
    }

    private static decimal? ResolveProvisionalUnitCost(OrderLine line)
    {
        if (line.ProvisionalUnitCost.HasValue && line.ProvisionalUnitCost.Value > 0)
            return line.ProvisionalUnitCost.Value;

        if (line.UnitCostSnapshot.HasValue && line.UnitCostSnapshot.Value > 0)
            return line.UnitCostSnapshot.Value;

        if (line.Variant?.CostPrice > 0)
            return line.Variant.CostPrice;

        return null;
    }

    private static void ApplyOrderLevelDiscount(
        IReadOnlyList<OrderLegalEntityAllocation> rows,
        decimal totalDiscount,
        Action<OrderLegalEntityAllocation, decimal> assign)
    {
        totalDiscount = RoundMoney(Math.Max(0m, totalDiscount));
        if (totalDiscount <= 0m)
            return;

        var weights = rows.Select(x => x.NetAmount).ToList();
        if (totalDiscount - weights.Sum() > MoneyTolerance)
            throw new InvalidOperationException("Giảm giá cấp đơn vượt quá tiền allocation còn lại.");

        var parts = DistributeMoney(totalDiscount, weights);
        for (var index = 0; index < rows.Count; index++)
        {
            var part = parts[index];
            assign(rows[index], part);
            rows[index].NetAmount = RoundMoney(rows[index].NetAmount - part);

            if (rows[index].NetAmount < -MoneyTolerance)
                throw new InvalidOperationException("Allocation có NetAmount âm sau phân bổ giảm giá.");

            if (rows[index].NetAmount < 0)
                rows[index].NetAmount = 0;
        }
    }

    private static decimal[] DistributeMoney(
        decimal total,
        IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0)
            return Array.Empty<decimal>();

        total = RoundMoney(Math.Max(0m, total));
        var normalizedWeights = weights.Select(x => Math.Max(0m, x)).ToArray();
        var totalWeight = normalizedWeights.Sum();
        var result = new decimal[weights.Count];

        if (total <= 0m)
            return result;

        if (totalWeight <= 0m)
            throw new InvalidOperationException("Không có trọng số dương để phân bổ tiền.");

        var lastPositiveIndex = Array.FindLastIndex(
            normalizedWeights,
            x => x > 0m);
        var remaining = total;
        for (var index = 0; index < result.Length; index++)
        {
            if (normalizedWeights[index] <= 0m)
                continue;

            if (index == lastPositiveIndex)
            {
                result[index] = remaining;
                break;
            }

            var part = RoundMoney(total * normalizedWeights[index] / totalWeight);
            part = Math.Min(part, remaining);
            result[index] = part;
            remaining = RoundMoney(remaining - part);
        }

        return result;
    }

    private static PosAppException ConfigurationError(string message)
        => PosAppException.StateConflict(
            PosErrorCodes.CheckoutLegalEntityConfigurationInvalid,
            "Cấu hình bán hàng nhiều HKD chưa hợp lệ.",
            "Vui lòng liên hệ quản lý để chạy lại activation preflight.",
            new { Detail = message });

    private static string TrimToLength(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static decimal RoundMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundUnitCost(decimal value)
        => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static decimal RoundCostAmount(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Services.Orders;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service xử lý reservation tồn kho.
///
/// Phase hiện tại:
/// - chỉ reserve khi Order chuyển sang OnHold
/// - Draft chưa reserve
/// - reservation làm thay đổi ReservedQty
/// - reservation không làm thay đổi OnHandQty
///
/// Quy tắc:
/// - Reserve  => tăng InventoryBalance.ReservedQty
/// - Release  => giảm InventoryBalance.ReservedQty
/// - Consume  => giảm InventoryBalance.ReservedQty và đánh dấu đã dùng
///
/// Rule policy:
/// - Nếu warehouse không cho âm kho thì reservation phải dựa trên AvailableQty
/// - Nếu warehouse cho âm kho thì reservation được phép vượt AvailableQty
///
/// Lưu ý:
/// - Service này xử lý trạng thái reserve, không xử lý ledger OnHand.
/// - Việc cộng/trừ OnHand vẫn thuộc InventoryMovementService.
/// - Transaction ngoài cùng nên nằm ở POSService.
/// </summary>
public class InventoryReservationService : IInventoryReservationService
{
    private sealed class ReservationPlan
    {
        public required IReadOnlyList<InventoryPostingLockKey> LockKeys { get; init; }
        public Warehouse? LegacyWarehouse { get; init; }
        public IReadOnlyList<OrderLine> LegacyLines { get; init; } = [];
        public OrderLegalEntityAllocationResult? MultiLegalEntityPreview { get; init; }
        public IReadOnlyDictionary<(int WarehouseId, int ProductVariantId), InventoryBalance>
            MultiLegalEntityBalances { get; init; } =
                new Dictionary<(int, int), InventoryBalance>();
        public IReadOnlyDictionary<int, LegalEntityAllocationSourceInput>
            SourcesByWarehouse { get; init; } =
                new Dictionary<int, LegalEntityAllocationSourceInput>();
        public IReadOnlyDictionary<int, OrderLine> LinesById { get; init; } =
            new Dictionary<int, OrderLine>();
        public DateTime ReservedAtUtc { get; init; }
    }

    private readonly IInventoryReservationRepository _inventoryReservationRepository;
    private readonly IInventoryBalanceRepository _inventoryBalanceRepository;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IPOSShiftRepository _shiftRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IOrderLegalEntityAllocationRepository _legalEntityRepository;
    private readonly IOrderLegalEntityAllocationService _allocationEngine;

    public InventoryReservationService(
        IInventoryReservationRepository inventoryReservationRepository,
        IInventoryBalanceRepository inventoryBalanceRepository,
        IInventoryMovementService inventoryMovementService,
        IPOSShiftRepository shiftRepository,
        IWarehouseRepository warehouseRepository,
        IOrderLegalEntityAllocationRepository legalEntityRepository,
        IOrderLegalEntityAllocationService allocationEngine)
    {
        _inventoryReservationRepository = inventoryReservationRepository;
        _inventoryBalanceRepository = inventoryBalanceRepository;
        _inventoryMovementService = inventoryMovementService;
        _shiftRepository = shiftRepository;
        _warehouseRepository = warehouseRepository;
        _legalEntityRepository = legalEntityRepository;
        _allocationEngine = allocationEngine;
    }

    public async Task ReserveForOrderAsync(Order order, CancellationToken ct = default)
    {
        ValidateOrder(order);

        if (order.Id <= 0)
            throw new InvalidOperationException("Order chưa hợp lệ để reserve.");

        // Idempotent cho thao tác giữ đơn bị double click/retry.
        if (await _inventoryReservationRepository.HasActiveByReferenceAsync(
                InventoryReferenceType.Order,
                order.Id.ToString(),
                ct))
        {
            order.HasReservation = true;
            order.ReservedAtUtc ??= DateTime.UtcNow;
            await _inventoryReservationRepository.SaveChangesAsync(ct);
            return;
        }

        var plan = await PrepareReservationPlanAsync(
            order,
            excludedReservations: [],
            checkExistingLegacyRows: true,
            ct);
        if (plan.LockKeys.Count > 0)
        {
            await _inventoryMovementService.PreLockBalancesAsync(
                plan.LockKeys,
                ct);
        }

        await ApplyReservationPlanAsync(order, plan, ct);
    }

    private async Task<ReservationPlan> PrepareReservationPlanAsync(
        Order order,
        IReadOnlyCollection<InventoryReservation> excludedReservations,
        bool checkExistingLegacyRows,
        CancellationToken ct)
    {
        var store = await _legalEntityRepository
            .GetStoreFeatureStateAsync(order.StoreId, ct);
        var now = DateTime.UtcNow;
        if (store != null && ShouldUseMultiLegalEntity(order, store, now))
        {
            return await PrepareMultiLegalEntityReservationPlanAsync(
                order,
                store,
                now,
                excludedReservations,
                ct);
        }

        var shift = await _shiftRepository.GetByIdAsync(order.POSShiftId, ct);
        if (shift == null)
            throw new InvalidOperationException("Không tìm thấy ca POS của order.");

        if (shift.WarehouseId <= 0)
            throw new InvalidOperationException("Ca POS chưa cấu hình kho xuất bán.");

        // Lấy kho thật từ repository để đọc policy chính xác.
        // Không nên phụ thuộc shift.Warehouse navigation vì có thể chưa Include.
        var warehouse = await _warehouseRepository.GetByIdAsync(shift.WarehouseId, ct);
        if (warehouse == null)
            throw new InvalidOperationException("Không tìm thấy kho xuất bán của ca POS.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho xuất bán đã ngưng hoạt động.");

        var warehouseId = warehouse.Id;

        var activeLines = order.Lines
            .Where(x => !x.IsDeleted)
            .Where(x => GetReserveQty(x) > 0)
            .ToList();

        if (!activeLines.Any())
            throw new InvalidOperationException("Order chưa có dòng hàng hợp lệ để giữ.");

        var reservableLines = new List<OrderLine>();
        foreach (var line in activeLines)
        {
            if (checkExistingLegacyRows)
            {
                var existed = await _inventoryReservationRepository
                    .ExistsActiveAsync(
                        InventoryReferenceType.Order,
                        order.Id.ToString(),
                        line.Id,
                        warehouseId,
                        line.VariantId,
                        ct);
                if (existed)
                    continue;
            }

            reservableLines.Add(line);
        }

        return new ReservationPlan
        {
            LegacyWarehouse = warehouse,
            LegacyLines = reservableLines,
            ReservedAtUtc = now,
            LockKeys = reservableLines
                .Select(x => new InventoryPostingLockKey(
                    order.StoreId,
                    warehouseId,
                    x.VariantId))
                .Distinct()
                .ToList()
        };
    }

    private async Task<ReservationPlan> PrepareMultiLegalEntityReservationPlanAsync(
        Order order,
        Store store,
        DateTime now,
        IReadOnlyCollection<InventoryReservation> excludedReservations,
        CancellationToken ct)
    {
        var activationAt = ResolveActivationAt(order, store);
        if (!activationAt.HasValue || activationAt.Value > now)
        {
            throw ConfigurationError(
                "Cửa hàng đã bật Multi LegalEntity nhưng chưa có ngày kích hoạt hợp lệ.");
        }

        var lines = order.Lines
            .Where(x => !x.IsDeleted && GetReserveQty(x) > 0)
            .OrderBy(x => x.Id)
            .ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Order chưa có dòng hàng hợp lệ để giữ.");

        var legalEntities = await _legalEntityRepository
            .GetActiveSalesLegalEntitiesAsync(order.StoreId, ct);
        var sources = BuildAndValidateSources(order.StoreId, legalEntities);
        var warehouseIds = sources.Select(x => x.WarehouseId).ToList();
        var variantIds = lines.Select(x => x.VariantId).Distinct().ToList();
        var balances = await _legalEntityRepository.LockInventoryForAllocationAsync(
            order.StoreId,
            warehouseIds,
            variantIds,
            ct);
        var excludedByKey = excludedReservations
            .GroupBy(x => (x.WarehouseId, x.ProductVariantId))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ReservedQty));

        var preview = _allocationEngine.Preview(new OrderLegalEntityAllocationRequest
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            Lines = lines.Select(x => new OrderLegalEntityAllocationLineInput
            {
                OrderLineId = x.Id,
                ProductVariantId = x.VariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                Quantity = x.Quantity,
                Multiplier = x.Multiplier,
                BaseQuantity = GetReserveQty(x)
            }).ToList(),
            EligibleSources = sources,
            Inventory = balances.Select(x => new LegalEntityInventoryAvailabilityInput
            {
                WarehouseId = x.WarehouseId,
                ProductVariantId = x.ProductVariantId,
                OnHandBaseQuantity = x.OnHandQty,
                ReservedBaseQuantity = Math.Max(
                    0m,
                    x.ReservedQty - excludedByKey.GetValueOrDefault(
                        (x.WarehouseId, x.ProductVariantId)))
            }).ToList()
        });

        if (!preview.IsSuccess)
        {
            throw PosAppException.StateConflict(
                PosErrorCodes.CheckoutLegalEntityInsufficientInventory,
                "Không đủ tồn khả dụng theo các HKD để giữ đơn.",
                "Vui lòng bổ sung/chuyển tồn hoặc giảm số lượng sản phẩm rồi giữ đơn lại.",
                new
                {
                    order.Id,
                    preview.TotalRequiredBaseQuantity,
                    preview.TotalAllocatedBaseQuantity,
                    preview.TotalShortageBaseQuantity,
                    preview.Shortages
                });
        }

        return new ReservationPlan
        {
            MultiLegalEntityPreview = preview,
            MultiLegalEntityBalances = balances.ToDictionary(
                x => (x.WarehouseId, x.ProductVariantId)),
            SourcesByWarehouse = sources.ToDictionary(x => x.WarehouseId),
            LinesById = lines.ToDictionary(x => x.Id),
            ReservedAtUtc = now,
            LockKeys = preview.Allocations
                .Select(x => new InventoryPostingLockKey(
                    order.StoreId,
                    x.WarehouseId,
                    x.ProductVariantId))
                .Distinct()
                .ToList()
        };
    }

    private async Task ApplyReservationPlanAsync(
        Order order,
        ReservationPlan plan,
        CancellationToken ct)
    {
        if (plan.MultiLegalEntityPreview is not null)
        {
            foreach (var allocation in plan.MultiLegalEntityPreview.Allocations)
            {
                if (!plan.MultiLegalEntityBalances.TryGetValue(
                        (allocation.WarehouseId, allocation.ProductVariantId),
                        out var balance))
                {
                    throw new InvalidOperationException(
                        $"Thiếu InventoryBalance đã khóa cho kho #{allocation.WarehouseId}, variant #{allocation.ProductVariantId}.");
                }

                balance.ReservedQty += allocation.BaseQuantity;
                var line = plan.LinesById[allocation.OrderLineId];
                var source = plan.SourcesByWarehouse[allocation.WarehouseId];
                await _inventoryReservationRepository.AddAsync(
                    new InventoryReservation
                    {
                        StoreId = order.StoreId,
                        WarehouseId = allocation.WarehouseId,
                        ProductVariantId = allocation.ProductVariantId,
                        ReferenceType = InventoryReferenceType.Order,
                        ReferenceId = order.Id.ToString(),
                        ReferenceLineId = allocation.OrderLineId,
                        ReservedQty = allocation.BaseQuantity,
                        Status = InventoryReservationStatus.Active,
                        Note =
                            $"Giữ hàng đa HKD cho đơn POS #{order.Id}, dòng {line.Id}. " +
                            $"HKD #{source.LegalEntityId}, ưu tiên {source.SalePriority}, SL gốc {allocation.BaseQuantity:0.###}.",
                        ReservedAtUtc = plan.ReservedAtUtc
                    },
                    ct);
            }
        }
        else
        {
            var warehouse = plan.LegacyWarehouse
                ?? throw new InvalidOperationException(
                    "Thiếu kho legacy trong reservation plan.");

            foreach (var line in plan.LegacyLines)
            {
                var reserveQty = GetReserveQty(line);
                var balance = await _inventoryBalanceRepository.GetOrCreateAsync(
                    warehouse.Id,
                    line.VariantId,
                    ct);
                if (!warehouse.AllowNegativeInventory &&
                    balance.AvailableQty < reserveQty)
                {
                    throw new InvalidOperationException(
                        $"Không đủ tồn khả dụng để giữ hàng. SP: {line.ItemName}, khả dụng: {balance.AvailableQty:0.###}, cần giữ: {reserveQty:0.###}.");
                }

                balance.ReservedQty += reserveQty;
                await _inventoryReservationRepository.AddAsync(
                    new InventoryReservation
                    {
                        StoreId = order.StoreId,
                        WarehouseId = warehouse.Id,
                        ProductVariantId = line.VariantId,
                        ReferenceType = InventoryReferenceType.Order,
                        ReferenceId = order.Id.ToString(),
                        ReferenceLineId = line.Id,
                        ReservedQty = reserveQty,
                        Status = InventoryReservationStatus.Active,
                        Note = BuildReserveNote(
                            order,
                            line,
                            reserveQty,
                            warehouse.AllowNegativeInventory),
                        ReservedAtUtc = plan.ReservedAtUtc
                    },
                    ct);
            }
        }

        order.HasReservation = true;
        order.ReservedAtUtc = plan.ReservedAtUtc;
        await _inventoryReservationRepository.SaveChangesAsync(ct);
    }

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

        return order.LegalEntityModeCapturedAtUtc.HasValue ||
               order.CreatedAtUtc == default ||
               order.CreatedAtUtc >= activationAt.Value;
    }

    private static DateTime? ResolveActivationAt(Order order, Store store)
        => order.LegalEntityModeCapturedAtUtc.HasValue && order.UseMultiLegalEntity
            ? order.LegalEntityActivationAtUtcSnapshot
            : store.MultiLegalEntityActivatedAtUtc;

    private static List<LegalEntityAllocationSourceInput> BuildAndValidateSources(
        int storeId,
        IReadOnlyCollection<LegalEntity> legalEntities)
    {
        if (legalEntities.Count < 2)
            throw ConfigurationError("Multi LegalEntity yêu cầu ít nhất hai HKD đang hoạt động.");

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

    private static PosAppException ConfigurationError(string message)
        => PosAppException.Business(
            PosErrorCodes.CheckoutLegalEntityConfigurationInvalid,
            message,
            "Vui lòng kiểm tra cấu hình HKD và kho mặc định trước khi giữ đơn.",
            statusCode: 409);

    public async Task ReleaseForOrderAsync(Order order, string? reason = null, CancellationToken ct = default)
    {
        ValidateOrder(order);

        var activeReservations = await _inventoryReservationRepository.GetActiveByReferenceAsync(
            InventoryReferenceType.Order,
            order.Id.ToString(),
            ct);

        if (!activeReservations.Any())
        {
            order.HasReservation = false;
            order.ReservedAtUtc = null;
            await _inventoryReservationRepository.SaveChangesAsync(ct);
            return;
        }

        var releaseReason = CleanText(reason, "Nhả giữ hàng.");
        var balanceKeys = activeReservations
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.WarehouseId,
                x.ProductVariantId))
            .Distinct()
            .ToList();
        await _inventoryMovementService.PreLockBalancesAsync(
            balanceKeys,
            ct);

        foreach (var reservation in activeReservations)
        {
            var balance = await _inventoryBalanceRepository.GetOrCreateAsync(
                reservation.WarehouseId,
                reservation.ProductVariantId,
                ct);

            balance.ReservedQty -= reservation.ReservedQty;
            if (balance.ReservedQty < 0)
                balance.ReservedQty = 0;

            reservation.Status = InventoryReservationStatus.Released;
            reservation.ReleasedAtUtc = DateTime.UtcNow;
            reservation.ReleaseNote = releaseReason;
        }

        order.HasReservation = false;
        order.ReservedAtUtc = null;

        await _inventoryReservationRepository.SaveChangesAsync(ct);
    }

    public async Task ConsumeForOrderAsync(Order order, CancellationToken ct = default)
    {
        ValidateOrder(order);

        var activeReservations = await _inventoryReservationRepository.GetActiveByReferenceAsync(
            InventoryReferenceType.Order,
            order.Id.ToString(),
            ct);

        if (!activeReservations.Any())
        {
            order.HasReservation = false;
            order.ReservedAtUtc = null;
            await _inventoryReservationRepository.SaveChangesAsync(ct);
            return;
        }

        var balanceKeys = activeReservations
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.WarehouseId,
                x.ProductVariantId))
            .Distinct()
            .ToList();
        await _inventoryMovementService.PreLockBalancesAsync(
            balanceKeys,
            ct);

        foreach (var reservation in activeReservations)
        {
            var balance = await _inventoryBalanceRepository.GetOrCreateAsync(
                reservation.WarehouseId,
                reservation.ProductVariantId,
                ct);

            balance.ReservedQty -= reservation.ReservedQty;
            if (balance.ReservedQty < 0)
                balance.ReservedQty = 0;

            reservation.Status = InventoryReservationStatus.Consumed;
            reservation.ReleasedAtUtc = DateTime.UtcNow;
            reservation.ReleaseNote = "Reservation đã được dùng khi finalize đơn hàng.";
        }

        order.HasReservation = false;
        order.ReservedAtUtc = null;

        await _inventoryReservationRepository.SaveChangesAsync(ct);
    }

    public async Task<bool> HasActiveReservationForOrderAsync(Order order, CancellationToken ct = default)
    {
        ValidateOrder(order);

        if (order.Id <= 0)
            return false;

        return await _inventoryReservationRepository.HasActiveByReferenceAsync(
            InventoryReferenceType.Order,
            order.Id.ToString(),
            ct);
    }

    private static void ValidateOrder(Order? order)
    {
        if (order == null)
            throw new InvalidOperationException("Order không hợp lệ.");
    }

    private static decimal GetReserveQty(OrderLine line)
    {
        // Reservation luôn tính theo đơn vị gốc.
        var reserveQty = line.BaseQuantity > 0 ? line.BaseQuantity : line.Quantity;
        return reserveQty < 0 ? 0 : reserveQty;
    }

    private static string BuildReserveNote(
        Order order,
        OrderLine line,
        decimal reserveQty,
        bool allowNegativeInventory)
    {
        var itemName = CleanText(line.ItemName, $"Variant #{line.VariantId}");
        var policyText = allowNegativeInventory
            ? "Kho cho phép âm."
            : "Kho không cho phép âm.";

        return $"Giữ hàng cho đơn POS #{order.Id}, dòng {line.Id}. SP: {itemName}, SL gốc: {reserveQty:0.###}. {policyText}";
    }

    private static string CleanText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public async Task RebuildForOrderAsync(Order order, CancellationToken ct = default)
    {
        ValidateOrder(order);

        var oldReservations = await _inventoryReservationRepository
            .GetActiveByReferenceAsync(
                InventoryReferenceType.Order,
                order.Id.ToString(),
                ct);
        var newPlan = await PrepareReservationPlanAsync(
            order,
            oldReservations,
            checkExistingLegacyRows: false,
            ct);

        var unionKeys = oldReservations
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.WarehouseId,
                x.ProductVariantId))
            .Concat(newPlan.LockKeys)
            .Distinct()
            .ToList();
        if (unionKeys.Count > 0)
        {
            await _inventoryMovementService.PreLockBalancesAsync(
                unionKeys,
                ct);
        }

        // Release old first, then reserve new, while holding the one canonical
        // old+new union batch acquired above.
        foreach (var reservation in oldReservations)
        {
            var balance = await _inventoryBalanceRepository.GetOrCreateAsync(
                reservation.WarehouseId,
                reservation.ProductVariantId,
                ct);
            balance.ReservedQty -= reservation.ReservedQty;
            if (balance.ReservedQty < 0)
                balance.ReservedQty = 0;

            reservation.Status = InventoryReservationStatus.Released;
            reservation.ReleasedAtUtc = DateTime.UtcNow;
            reservation.ReleaseNote =
                "Nhả giữ hàng cũ để cập nhật lại theo giỏ hàng hiện tại.";
        }

        order.HasReservation = false;
        order.ReservedAtUtc = null;
        await ApplyReservationPlanAsync(order, newPlan, ct);
    }
}

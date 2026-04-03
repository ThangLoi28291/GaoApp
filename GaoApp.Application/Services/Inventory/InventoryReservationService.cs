using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

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
    private readonly IInventoryReservationRepository _inventoryReservationRepository;
    private readonly IInventoryBalanceRepository _inventoryBalanceRepository;
    private readonly IPOSShiftRepository _shiftRepository;
    private readonly IWarehouseRepository _warehouseRepository;

    public InventoryReservationService(
        IInventoryReservationRepository inventoryReservationRepository,
        IInventoryBalanceRepository inventoryBalanceRepository,
        IPOSShiftRepository shiftRepository,
        IWarehouseRepository warehouseRepository)
    {
        _inventoryReservationRepository = inventoryReservationRepository;
        _inventoryBalanceRepository = inventoryBalanceRepository;
        _shiftRepository = shiftRepository;
        _warehouseRepository = warehouseRepository;
    }

    public async Task ReserveForOrderAsync(Order order, CancellationToken ct = default)
    {
        ValidateOrder(order);

        if (order.Id <= 0)
            throw new InvalidOperationException("Order chưa hợp lệ để reserve.");

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
        var allowNegativeInventory = warehouse.AllowNegativeInventory;

        var activeLines = order.Lines
            .Where(x => !x.IsDeleted)
            .Where(x => GetReserveQty(x) > 0)
            .ToList();

        if (!activeLines.Any())
            throw new InvalidOperationException("Order chưa có dòng hàng hợp lệ để giữ.");

        foreach (var line in activeLines)
        {
            var reserveQty = GetReserveQty(line);

            var existed = await _inventoryReservationRepository.ExistsActiveAsync(
                InventoryReferenceType.Order,
                order.Id.ToString(),
                line.Id,
                warehouseId,
                line.VariantId,
                ct);

            if (existed)
                continue;

            var balance = await _inventoryBalanceRepository.GetOrCreateAsync(
                warehouseId,
                line.VariantId,
                ct);

            // Rule policy:
            // - Kho không cho âm => reservation phải đủ AvailableQty
            // - Kho cho âm      => cho phép reserve vượt AvailableQty
            if (!allowNegativeInventory && balance.AvailableQty < reserveQty)
            {
                throw new InvalidOperationException(
                    $"Không đủ tồn khả dụng để giữ hàng. SP: {line.ItemName}, khả dụng: {balance.AvailableQty:0.###}, cần giữ: {reserveQty:0.###}.");
            }

            balance.ReservedQty += reserveQty;

            var reservation = new InventoryReservation
            {
                WarehouseId = warehouseId,
                ProductVariantId = line.VariantId,
                ReferenceType = InventoryReferenceType.Order,
                ReferenceId = order.Id.ToString(),
                ReferenceLineId = line.Id,
                ReservedQty = reserveQty,
                Status = InventoryReservationStatus.Active,
                Note = BuildReserveNote(order, line, reserveQty, allowNegativeInventory),
                ReservedAtUtc = DateTime.UtcNow
            };

            await _inventoryReservationRepository.AddAsync(reservation, ct);
        }

        await _inventoryReservationRepository.SaveChangesAsync(ct);
    }

    public async Task ReleaseForOrderAsync(Order order, string? reason = null, CancellationToken ct = default)
    {
        ValidateOrder(order);

        var activeReservations = await _inventoryReservationRepository.GetActiveByReferenceAsync(
            InventoryReferenceType.Order,
            order.Id.ToString(),
            ct);

        if (!activeReservations.Any())
            return;

        var releaseReason = CleanText(reason, "Nhả giữ hàng.");

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
            return;

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

        // Nếu đang có reservation active cũ thì nhả hết trước
        if (await HasActiveReservationForOrderAsync(order, ct))
        {
            await ReleaseForOrderAsync(order, "Nhả giữ hàng cũ để cập nhật lại theo giỏ hàng hiện tại.", ct);
        }

        // Sau đó reserve lại theo line hiện tại
        await ReserveForOrderAsync(order, ct);
    }
}
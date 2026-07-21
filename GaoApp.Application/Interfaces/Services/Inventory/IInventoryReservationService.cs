using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryReservationService
{
    /// <summary>
    /// Tạo reservation cho toàn bộ line của order.
    /// Phase hiện tại dùng khi chuyển Draft -> OnHold.
    /// </summary>
    Task ReserveForOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Nhả reservation đang active của order.
    /// Dùng khi hủy đơn giữ hoặc bỏ giữ.
    /// </summary>
    Task ReleaseForOrderAsync(Order order, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Tiêu thụ reservation của order khi đi tới finalize.
    /// Sau bước này ReservedQty phải được giảm tương ứng.
    /// </summary>
    Task ConsumeForOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra order còn reservation active hay không.
    /// </summary>
    Task<bool> HasActiveReservationForOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Dựng lại toàn bộ reservation của order theo snapshot line hiện tại.
    /// Dùng khi order đã từng OnHold, được Resume, sau đó thay đổi số lượng rồi Hold lại.
    /// </summary>
    Task RebuildForOrderAsync(Order order, CancellationToken ct = default);
}
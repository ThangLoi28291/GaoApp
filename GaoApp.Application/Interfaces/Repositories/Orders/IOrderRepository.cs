using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderRepository
{
    Task<Order?> GetDraftAsync(int orderId, CancellationToken ct = default);

    Task<OrderLine?> GetDraftLineAsync(int lineId, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Update order hiện có.
    /// Dùng cho các flow mirror trạng thái inventory issue về Order.
    /// </summary>
    void Update(Order order);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<Order?> GetByIdAsync(int orderId, CancellationToken ct = default);

    Task<(List<Order> Items, int Total)> QueryOrdersAsync(
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        OrderStatus? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsDraftByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<int> CountDraftByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<List<Order>> GetByShiftIdAsync(int shiftId, CancellationToken ct = default);

    Task<List<Order>> GetHeldOrdersByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<List<Order>> GetDraftOrdersByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<Order?> GetByIdWithDetailsAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetCompletedOrderForVoidAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetCompletedOrderForRefundAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetDraftForFinalizeAsync(int orderId, CancellationToken ct = default);
}
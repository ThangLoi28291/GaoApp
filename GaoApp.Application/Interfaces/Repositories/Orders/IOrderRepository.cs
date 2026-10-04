using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderRepository
{
    Task<Order?> GetDraftAsync(int orderId, CancellationToken ct = default);

    Task<OrderLine?> GetDraftLineAsync(int lineId, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    void Update(Order order);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<Order?> GetByIdAsync(int orderId, CancellationToken ct = default);

    // Call inside the issuance store lock; refresh tracked scalar values and concurrency token.
    Task<Order?> GetForInvoiceRouteChangeAsync(int storeId, int orderId, CancellationToken ct = default);

    Task<(List<GaoApp.Application.DTOs.POS.OrderListItemDto> Items, int Total)> QueryOrdersAsync(
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        OrderStatus? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct = default,
        GaoApp.Application.DTOs.POS.OrderListQueryDto? filters = null);

    Task<HashSet<int>> GetBankTransferOrderIdsAsync(IReadOnlyCollection<int> orderIds, CancellationToken ct = default);

    Task<List<GaoApp.Application.DTOs.POS.OrderFilterOptionDto>> GetListFilterOptionsAsync(bool employees, string? term, CancellationToken ct = default);

    Task<bool> ExistsDraftByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<int> CountDraftByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<List<Order>> GetByShiftIdAsync(int shiftId, CancellationToken ct = default);

    Task<List<Order>> GetHeldOrdersByShiftAsync(int shiftId, CancellationToken ct = default);

    // NEW
    Task<List<Order>> GetHeldOrdersByStoreAsync(int storeId, CancellationToken ct = default);

    Task<List<Order>> GetDraftOrdersByShiftAsync(int shiftId, CancellationToken ct = default);

    Task<Order?> GetByIdWithDetailsAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetCompletedOrderForVoidAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetCompletedOrderForRefundAsync(int orderId, CancellationToken ct = default);

    Task<Order?> GetDraftForFinalizeAsync(int orderId, CancellationToken ct = default);
    Task ReplaceRewardVouchersAsync(
    int orderId,
    List<OrderRewardVoucher> vouchers,
    CancellationToken ct = default);

    Task ClearRewardVouchersAsync(
        int orderId,
        CancellationToken ct = default);
}

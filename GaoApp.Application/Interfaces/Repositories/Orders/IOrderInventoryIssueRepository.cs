using GaoApp.Application.DTOs.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderInventoryIssueRepository
{
    Task<OrderInventoryIssue?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy detail đầy đủ của case:
    /// - Order
    /// - Lines
    /// - Actions
    /// - User approve/reject
    /// </summary>
    Task<OrderInventoryIssue?> GetDetailByIdAsync(int id, CancellationToken ct = default);

    Task<OrderInventoryIssue?> GetByOrderIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách case để hiển thị màn hình quản lý.
    /// Quy ước sort:
    /// 1. Overdue trước
    /// 2. Overdue lâu hơn lên trước
    /// 3. Case mở cũ hơn lên trước
    /// </summary>
    Task<List<OrderInventoryIssue>> GetListAsync(
        InventoryResolutionStatus? status = null,
        bool? onlyOverdue = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các case còn mở để xử lý nghiệp vụ.
    /// Chỉ gồm:
    /// - PendingResolution
    /// - ReadyForApproval
    /// 
    /// Không lấy Approved / Rejected.
    /// </summary>
    Task<List<OrderInventoryIssue>> GetPendingOrReadyIssuesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy các case còn mở và đã tới hạn / quá hạn theo mốc nowUtc.
    /// Dùng cho service refresh overdue để hạn chế quét toàn bộ bảng.
    /// </summary>
    Task<List<OrderInventoryIssue>> GetOverdueCandidatesAsync(DateTime nowUtc, CancellationToken ct = default);

    Task AddAsync(OrderInventoryIssue entity, CancellationToken ct = default);

    void Update(OrderInventoryIssue entity);

    Task AddActionAsync(OrderInventoryIssueAction entity, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Mức 2:
    /// Lấy tồn available hiện tại của variant trong đúng kho của issue line.
    /// Repository chịu trách nhiệm join đúng kho/ngữ cảnh hiện tại.
    /// </summary>
    Task<decimal> GetCurrentAvailableQtyForIssueLineAsync(int issueLineId, CancellationToken ct = default);

    Task<InventoryIssueCostResolutionSnapshotDto> GetCostResolutionSnapshotForOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    Task<List<InventoryIssueDocumentOptionDto>> GetReceiptOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default);

    Task<List<InventoryIssueDocumentOptionDto>> GetAdjustmentOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default);

    Task<List<InventoryIssueInboundCandidateDto>> GetInboundCandidatesForIssueAsync(
        int issueId,
        CancellationToken ct = default);

    Task<List<OrderInventoryIssueLineAllocation>> GetAllocationsByIssueAsync(
        int issueId,
        CancellationToken ct = default);

    Task ReplaceAllocationsAsync(
        int issueId,
        List<OrderInventoryIssueLineAllocation> allocations,
        CancellationToken ct = default);
}
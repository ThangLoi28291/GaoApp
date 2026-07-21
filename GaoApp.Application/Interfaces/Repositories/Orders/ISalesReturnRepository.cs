using GaoApp.Application.DTOs.Returns;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

/// <summary>
/// Repository đọc/ghi SalesReturn phục vụ nghiệp vụ trả hàng / hoàn tiền.
///
/// Ghi chú:
/// - Repository này chỉ lo truy xuất dữ liệu
/// - Không chứa business rule tính costing
/// - SalesReturnService mức 2 sẽ orchestration:
///   + restock / non-restock
///   + mirror valuation từ order line gốc
///   + refund payment
/// </summary>
public interface ISalesReturnRepository
{
    /// <summary>
    /// Thêm mới phiếu trả hàng.
    /// </summary>
    Task AddAsync(SalesReturn entity, CancellationToken ct = default);

    /// <summary>
    /// Lấy phiếu trả hàng theo id.
    /// </summary>
    Task<SalesReturn?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy phiếu trả hàng kèm details.
    ///
    /// Dùng cho:
    /// - xem chi tiết phiếu
    /// - in phiếu
    /// - xử lý nghiệp vụ sau khi tạo / hoàn tất
    /// </summary>
    Task<SalesReturn?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ phiếu trả hàng theo order gốc.
    /// </summary>
    Task<List<SalesReturn>> GetByOrderIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Tổng số lượng đã trả theo đơn vị bán của 1 order line.
    ///
    /// Dùng cho các màn hình cũ hoặc rule đang dựa trên ReturnQuantity.
    /// </summary>
    Task<decimal> GetReturnedQuantityByOrderLineAsync(int orderLineId, CancellationToken ct = default);

    /// <summary>
    /// Tổng số lượng đã trả theo đơn vị gốc (base quantity) của 1 order line.
    ///
    /// Đây là method quan trọng cho mức 2 vì:
    /// - partial return phải chặn vượt quá base qty đã bán
    /// - mirror valuation / inventory movement đều nên bám base qty
    /// - không phụ thuộc đơn vị bán hiện tại
    /// </summary>
    Task<decimal> GetReturnedBaseQuantityByOrderLineAsync(int orderLineId, CancellationToken ct = default);

    /// <summary>
    /// Tổng tiền đã refund của toàn bộ sales return completed thuộc 1 order.
    /// Dùng để không refund vượt giá trị đơn gốc.
    /// </summary>
    Task<decimal> GetRefundedTotalByOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra 1 order line đã từng có sales return completed hay chưa.
    ///
    /// Hữu ích cho:
    /// - audit
    /// - hiển thị badge hậu mãi
    /// - một số rule tối ưu ở service
    /// </summary>
    Task<bool> HasCompletedReturnByOrderLineAsync(int orderLineId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra order đã có bất kỳ phiếu return completed nào hay chưa.
    /// Void toàn bộ phải bị chặn sau partial return để không hoàn tồn/tiền hai lần.
    /// </summary>
    Task<bool> HasCompletedReturnByOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Lấy các sales return line completed của 1 order line.
    ///
    /// Dùng khi service cần:
    /// - audit lịch sử return
    /// - phân tích partial return trước đó
    /// - đối soát source line
    /// </summary>
    Task<List<SalesReturnLine>> GetCompletedReturnLinesByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy tổng base quantity đã restock trở lại kho của 1 order line.
    ///
    /// Quan trọng để phân biệt:
    /// - đã trả nhưng không nhập kho lại (non-restock)
    /// - đã trả và nhập kho lại (restock)
    ///
    /// Mức 2 cần tách rõ để movement/valuation không bị sai ngữ nghĩa.
    /// </summary>
    Task<decimal> GetRestockedBaseQuantityByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy tổng base quantity đã trả nhưng không nhập kho lại của 1 order line.
    ///
    /// Dùng cho:
    /// - hàng hỏng / không nhận lại kho
    /// - đối soát hậu mãi
    /// - báo cáo phân loại restock / non-restock
    /// </summary>
    Task<decimal> GetNonRestockedBaseQuantityByOrderLineAsync(
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Tổng hợp hậu mãi cho nhiều order cùng lúc để tránh N+1 query.
    /// Chỉ lấy các phiếu completed, chưa bị xóa mềm.
    /// </summary>
    Task<List<OrderAfterSaleSummaryDto>> GetAfterSaleSummaryByOrderIdsAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken ct = default);
}

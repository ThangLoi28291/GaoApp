using GaoApp.Application.DTOs.Returns;

namespace GaoApp.Application.Interfaces.Services.Orders;

/// <summary>
/// Service xử lý nghiệp vụ trả hàng / hoàn tiền.
///
/// Mức 2:
/// - hỗ trợ restock / non-restock
/// - mirror valuation từ order line gốc
/// - không dùng average cost hiện tại
/// - giữ trace provisional / revaluation nếu dòng gốc có
/// - partial return đúng theo base quantity và source fragment
/// - refund payment đúng logic
///
/// Lưu ý:
/// - interface này giữ shape đơn giản, không ép thay DTO ngay lúc này
/// - business flow chi tiết sẽ nằm ở implementation
/// </summary>
public interface ISalesReturnService
{
    /// <summary>
    /// Tạo phiếu sales return.
    ///
    /// Flow implementation sẽ bao gồm:
    /// - validate order / line / quantity đủ điều kiện return
    /// - phân loại restock / non-restock theo từng line
    /// - nếu restock thì tạo inventory movement phù hợp
    /// - mirror valuation từ order line gốc
    /// - xử lý refund payment
    /// </summary>
    Task<SalesReturnDto> CreateAsync(CreateSalesReturnRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lấy phiếu sales return theo id.
    /// </summary>
    Task<SalesReturnDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách sales return theo order gốc.
    /// </summary>
    Task<List<SalesReturnDto>> GetByOrderIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra eligibility của 1 order trước khi cho phép tạo sales return.
    /// 
    /// Thường gồm:
    /// - order có tồn tại không
    /// - trạng thái order có cho phép return không
    /// - line nào còn qty có thể trả
    /// - tổng refundable amount còn bao nhiêu
    /// </summary>
    Task<OrderReturnEligibilityDto> GetEligibilityAsync(int orderId, CancellationToken ct = default);
}
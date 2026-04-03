using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service xử lý finalize provisional cost theo inbound FIFO layer thật.
///
/// Mức 2:
/// - KHÔNG revalue toàn bộ provisional open theo 1 giá inbound global
/// - Mỗi inbound layer resolve dần provisional allocations theo FIFO thời gian outbound
/// - Layer nào hết qty thì mới chuyển layer tiếp theo
/// </summary>
public interface IInventoryRevaluationService
{
    /// <summary>
    /// Resolve provisional outbound bằng 1 inbound layer thật.
    ///
    /// Quy tắc:
    /// - lấy inbound layer theo id
    /// - dùng RemainingQuantity của layer đó để resolve dần
    ///   các provisional allocations còn mở
    /// - đi theo FIFO thời gian outbound
    /// - mỗi phần resolve sinh revaluation adjustment riêng
    /// - không double apply
    /// </summary>
    Task<List<ProvisionalRevaluationPlanDto>> ResolveByInboundLayerAsync(
        int inboundLayerId,
        DateTime occurredAtUtc,
        string note,
        CancellationToken ct = default);
}
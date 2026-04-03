namespace GaoApp.Application.DTOs.Returns;

/// <summary>
/// Tổng hợp hậu mãi theo từng order để gắn ra màn danh sách đơn.
/// </summary>
public sealed class OrderAfterSaleSummaryDto
{
    public int OrderId { get; set; }

    /// <summary>
    /// Tổng tiền đã hoàn của các phiếu return/refund completed.
    /// </summary>
    public decimal RefundedTotal { get; set; }

    /// <summary>
    /// Số phiếu return/refund completed.
    /// </summary>
    public int ReturnCount { get; set; }
}
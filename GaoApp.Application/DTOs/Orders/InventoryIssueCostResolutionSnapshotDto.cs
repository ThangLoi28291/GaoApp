namespace GaoApp.Application.DTOs.Orders;

/// <summary>
/// Snapshot phần giá vốn dùng cho resolve Inventory Issue line.
/// </summary>
public sealed class InventoryIssueCostResolutionSnapshotDto
{
    /// <summary>
    /// Có phát sinh entry revaluation/finalization cho order line hay chưa.
    /// </summary>
    public bool HasRevaluationEntry { get; set; }

    /// <summary>
    /// Tổng amount của các entry revaluation cho order line.
    /// Có thể dương / âm / 0 tùy chênh lệch giá vốn.
    /// </summary>
    public decimal RevaluationAmount { get; set; }

    /// <summary>
    /// Thời điểm revaluation mới nhất.
    /// </summary>
    public DateTime? LastRevaluationAtUtc { get; set; }
}
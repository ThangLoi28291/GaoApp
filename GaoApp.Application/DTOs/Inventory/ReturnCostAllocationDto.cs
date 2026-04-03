namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Kết quả phân bổ cost cho 1 lần return.
/// Mỗi allocation tương ứng với 1 source valuation fragment gốc.
/// </summary>
public class ReturnCostAllocationDto
{
    /// <summary>
    /// Source valuation entry gốc bị reverse.
    /// </summary>
    public int SourceValuationEntryId { get; set; }

    /// <summary>
    /// SubKey của source fragment gốc.
    /// </summary>
    public string? SourceReferenceSubKey { get; set; }

    /// <summary>
    /// Số lượng reverse ở allocation này.
    /// Luôn là số dương.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Unit cost mirror từ source fragment gốc.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// True nếu source fragment gốc là provisional.
    /// </summary>
    public bool IsProvisional { get; set; }
}
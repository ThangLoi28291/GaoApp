namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Biểu diễn 1 source valuation fragment của sale issue
/// và phần còn lại có thể dùng để return/void.
/// 
/// Quy ước:
/// - SourceQuantityAbs: số lượng gốc của fragment, luôn lấy giá trị tuyệt đối
/// - ReversedQuantityAbs: tổng số lượng đã reverse trước đó
/// - RemainingQuantityAbs: số lượng còn lại có thể reverse
/// </summary>
public class ReturnableValuationFragmentDto
{
    /// <summary>
    /// Id của source valuation entry gốc.
    /// </summary>
    public int SourceValuationEntryId { get; set; }

    /// <summary>
    /// SubKey của source fragment.
    /// Dùng để debug/audit nhanh.
    /// </summary>
    public string? ReferenceSubKey { get; set; }

    /// <summary>
    /// Số lượng gốc của fragment.
    /// Luôn lưu theo trị tuyệt đối để service dễ dùng.
    /// </summary>
    public decimal SourceQuantityAbs { get; set; }

    /// <summary>
    /// Tổng số lượng đã reverse từ fragment này trước đó.
    /// </summary>
    public decimal ReversedQuantityAbs { get; set; }

    /// <summary>
    /// Số lượng còn lại có thể reverse.
    /// </summary>
    public decimal RemainingQuantityAbs { get; set; }

    /// <summary>
    /// Unit cost của source fragment gốc.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// True nếu source fragment gốc là provisional.
    /// </summary>
    public bool IsProvisional { get; set; }

    /// <summary>
    /// Thời điểm phát sinh source fragment.
    /// Dùng để giữ thứ tự allocation ổn định.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }
}
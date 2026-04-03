namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Kế hoạch revaluation cho 1 phần provisional allocation
/// được resolve bởi 1 inbound FIFO layer thật.
/// 
/// Lưu ý:
/// - Không còn tư duy "1 provisional entry = 1 final inbound cost"
/// - 1 provisional entry có thể được resolve nhiều lần
///   bởi nhiều inbound layer khác nhau
/// - DTO này biểu diễn 1 fragment resolve cụ thể
/// </summary>
public class ProvisionalRevaluationPlanDto
{
    /// <summary>
    /// Provisional valuation entry gốc cần finalize.
    /// </summary>
    public int ProvisionalEntryId { get; set; }

    /// <summary>
    /// Provisional allocation cụ thể đang được resolve.
    /// Có thể null trong một số case legacy/backfill,
    /// nhưng với flow mới thì nên có.
    /// </summary>
    public int? ProvisionalAllocationId { get; set; }

    /// <summary>
    /// Inbound FIFO layer dùng để resolve.
    /// </summary>
    public int InboundLayerId { get; set; }

    /// <summary>
    /// Số lượng được resolve ở fragment này.
    /// Luôn là số dương để dễ tính.
    /// </summary>
    public decimal QuantityAbs { get; set; }

    /// <summary>
    /// Provisional unit cost đã ghi nhận trước đó.
    /// </summary>
    public decimal ProvisionalUnitCost { get; set; }

    /// <summary>
    /// Actual unit cost của inbound layer thật dùng để resolve.
    /// </summary>
    public decimal FinalUnitCost { get; set; }

    /// <summary>
    /// Chênh lệch đơn giá.
    /// Final - Provisional.
    /// </summary>
    public decimal UnitCostDelta { get; set; }

    /// <summary>
    /// Giá trị revaluation adjustment của fragment resolve này.
    /// Có thể âm hoặc dương tùy cost tăng hay giảm.
    /// </summary>
    public decimal RevaluationAmount { get; set; }
}
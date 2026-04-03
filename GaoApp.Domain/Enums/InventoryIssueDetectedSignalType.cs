namespace GaoApp.Domain.Enums;

/// <summary>
/// Tín hiệu ban đầu dùng để mở case inventory issue.
/// Lưu ý: đây KHÔNG phải nguyên nhân gốc cuối cùng.
/// </summary>
public enum InventoryIssueDetectedSignalType
{
    None = 0,

    /// <summary>
    /// Phát hiện âm tồn ngay sau finalize.
    /// </summary>
    NegativeInventoryDetected = 1,

    /// <summary>
    /// Phát hiện có valuation provisional khi finalize.
    /// </summary>
    ProvisionalCostDetected = 2,

    /// <summary>
    /// Đồng thời có cả âm tồn và provisional cost.
    /// </summary>
    MixedDetected = 3
}
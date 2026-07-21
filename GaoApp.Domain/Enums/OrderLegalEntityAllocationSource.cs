namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguồn quyết định phân bổ dòng bán sang LegalEntity.
/// Phase 22.4 chỉ sinh phân bổ tự động theo SalePriority.
/// </summary>
public enum OrderLegalEntityAllocationSource
{
    AutoBySalePriority = 1,
    ManualOverride = 2,

    /// <summary>
    /// Phần thiếu sau khi đã dùng hết tồn khả dụng của mọi HKD.
    /// Phần này được ghi vào HKD cuối chuỗi ưu tiên và tạo inventory issue.
    /// </summary>
    AutoNegativeFallback = 3
}

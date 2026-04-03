namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại chứng từ kho.
/// Phase 5.4 hiện tại dùng Receipt,
/// nhưng thiết kế sẵn cho các phase sau.
/// </summary>
public enum StockDocumentType
{
    Receipt = 1,   // Nhập kho
    Issue = 2,     // Xuất kho
    Transfer = 3,  // Chuyển kho
    Adjustment = 4 // Điều chỉnh
}
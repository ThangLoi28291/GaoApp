namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO hiển thị lịch sử barcode theo ProductUnitConversion.
/// Dùng cho modal quản lý barcode.
/// </summary>
public sealed class ProductVariantBarcodeHistoryRowDto
{
    public int Id { get; set; }
    public int ProductUnitConversionId { get; set; }

    /// <summary>
    /// Tên enum: Assigned / Replaced / Deactivated / Reactivated / Imported
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Nhãn tiếng Việt hiển thị trên UI.
    /// </summary>
    public string ActionTypeText { get; set; } = string.Empty;

    public string? OldBarcode { get; set; }
    public string? NewBarcode { get; set; }

    public string? Reason { get; set; }
    public string? ChangedByUserName { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
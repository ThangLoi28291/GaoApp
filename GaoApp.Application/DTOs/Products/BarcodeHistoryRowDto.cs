namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO dòng lịch sử barcode dùng cho màn hình tra cứu tổng.
/// </summary>
public sealed class BarcodeHistoryRowDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantSku { get; set; }
    public string? VariantName { get; set; }

    public int UnitId { get; set; }
    public string? UnitName { get; set; }

    public string ActionType { get; set; } = string.Empty;
    public string ActionTypeText { get; set; } = string.Empty;

    public string? OldBarcode { get; set; }
    public string? NewBarcode { get; set; }

    public string? Reason { get; set; }

    public int? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
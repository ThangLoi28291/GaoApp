namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// Request filter cho màn hình lịch sử barcode.
/// </summary>
public sealed class BarcodeHistoryQueryRequest
{
    public string? Keyword { get; set; }

    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Assigned / Replaced / Deactivated / Reactivated / Imported
    /// </summary>
    public string? ActionType { get; set; }

    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class StockDocumentLineInputInvoiceMapDto
{
    public int StockDocumentLineId { get; set; }
    public int StockDocumentId { get; set; }

    public bool UseInputInvoice { get; set; }

    public int? InputInvoiceDetailId { get; set; }

    public InputInvoiceMatchStatus MatchStatus { get; set; }

    public decimal QuantityDifference { get; set; }
    public decimal AmountDifference { get; set; }
    public string? ExclusionReason { get; set; }

    public string? XmlItemName { get; set; }
    public string? XmlUnitName { get; set; }
    public decimal? XmlQuantity { get; set; }
    public decimal? XmlLineAmount { get; set; }
    public InputInvoiceItemCatalogResolutionDto? ItemCatalogMapping { get; set; }
}

public sealed class UpdateStockDocumentLineInputInvoiceMapRequest
{
    public int StockDocumentLineId { get; set; }

    /// <summary>
    /// true: dòng nhập này thuộc hóa đơn XML.
    /// false: dòng này không thuộc XML.
    /// </summary>
    public bool UseInputInvoice { get; set; }

    /// <summary>
    /// Nullable vì nếu UseInputInvoice=false thì không cần chọn XML detail.
    /// </summary>
    public int? InputInvoiceDetailId { get; set; }

    /// <summary>
    /// Khi true, xác nhận thêm mapping danh mục bền vững bằng target được lấy
    /// server-side từ StockDocumentLine. Client không có quyền chọn target.
    /// </summary>
    public bool RememberItemCatalogMapping { get; set; }

    // Legacy compatibility only. Ordinary line linking derives these values
    // from StockDocumentLine and never trusts values posted by the browser.
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public string? MappingRowVersion { get; set; }
    public string? ExclusionReason { get; set; }
}
public sealed class BulkUpdateStockDocumentLineInputInvoiceRequest
{
    /// <summary>
    /// true  = tất cả dòng nhập thuộc XML
    /// false = tất cả dòng nhập không thuộc XML
    /// </summary>
    public bool UseInputInvoice { get; set; }
    public string? ExclusionReason { get; set; }
}

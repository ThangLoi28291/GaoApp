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

    public string? XmlItemName { get; set; }
    public string? XmlUnitName { get; set; }
    public decimal? XmlQuantity { get; set; }
    public decimal? XmlLineAmount { get; set; }
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
}
public sealed class BulkUpdateStockDocumentLineInputInvoiceRequest
{
    /// <summary>
    /// true  = tất cả dòng nhập thuộc XML
    /// false = tất cả dòng nhập không thuộc XML
    /// </summary>
    public bool UseInputInvoice { get; set; }
}
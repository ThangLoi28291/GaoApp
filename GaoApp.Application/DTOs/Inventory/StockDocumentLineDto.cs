using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentLineDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }

    public int ProductVariantId { get; set; }

    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }

    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>
    /// Ảnh sản phẩm dùng cho UI dòng nhập kho.
    /// </summary>
    public string? ProductImageUrl { get; set; }
    public string ProductNameSnapshot { get; set; } = default!;
    public string? SkuSnapshot { get; set; }
    public string? BarcodeSnapshot { get; set; }

    public string? Note { get; set; }
    public bool UseInputInvoice { get; set; }
    public int? InputInvoiceDetailId { get; set; }
    public string? XmlItemName { get; set; }
    public decimal? XmlQuantity { get; set; }
    public decimal? XmlLineAmount { get; set; }
    public InputInvoiceMatchStatus? InputInvoiceMatchStatus { get; set; }
    public decimal QuantityDifference { get; set; }
    public decimal AmountDifference { get; set; }
    public List<StockDocumentUnitOptionDto> AvailableUnits { get; set; } = new();
}
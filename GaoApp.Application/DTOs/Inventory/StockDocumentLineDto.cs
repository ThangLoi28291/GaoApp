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
    public int? PurchaseOrderLineId { get; set; }
    public decimal PurchaseOrderCanonicalOrderedQuantity { get; set; }
    public decimal PurchaseOrderCanonicalConfirmedQuantity { get; set; }
    public decimal ProjectedOverdeliveryQuantity { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? TaxId { get; set; }
    /// <summary>
    /// Thuế đang cấu hình trên sản phẩm, chỉ dùng để gợi ý khi quản lý chốt VAT.
    /// Snapshot TaxId của dòng vẫn là dữ liệu được duyệt chính thức.
    /// </summary>
    public int? SuggestedTaxId { get; set; }
    public string? TaxNameSnapshot { get; set; }
    public decimal UnitPriceBeforeVat { get; set; }
    /// <summary>
    /// Giá chưa VAT của lần nhập đã duyệt gần nhất, quy đổi sang đúng đơn vị
    /// của dòng hiện tại. Chỉ dùng hiển thị/so sánh; không dùng để ghi sổ.
    /// </summary>
    public decimal? LastPurchaseUnitPriceBeforeVat { get; set; }
    public decimal TaxRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal UnitPriceAfterVat { get; set; }
    public decimal FreightAllocation { get; set; }
    public PurchaseShortageDisposition ShortageDisposition { get; set; }
    public string? ShortageReason { get; set; }
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

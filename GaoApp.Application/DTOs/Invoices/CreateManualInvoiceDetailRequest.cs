namespace GaoApp.Application.DTOs.Invoices;

public class CreateManualInvoiceDetailRequest
{
    public int InvoiceHeadId { get; set; }

    /// <summary>
    /// Cho phép null để nhập dòng tự do.
    /// Sau này nếu muốn chọn sản phẩm thì truyền ProductVariantId.
    /// </summary>
    public int? ProductVariantId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }

    public string? Note { get; set; }
}
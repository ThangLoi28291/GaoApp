namespace GaoApp.Web.ViewModels.Invoices;

public class CreateManualInvoiceDetailViewModel
{
    public int InvoiceHeadId { get; set; }

    // Dòng manual nhưng vẫn cho chọn ProductVariant
    public int? ProductVariantId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    public decimal Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }

    public string? Note { get; set; }
}
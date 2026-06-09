namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceProductVariantSearchItemDto
{
    public int ProductVariantId { get; set; }

    public int ProductId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public string? Barcode { get; set; }

    public string? UnitName { get; set; }

    public decimal UnitPrice { get; set; }
}
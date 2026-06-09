using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceDetailDto
{
    public int Id { get; set; }

    public int InvoiceHeadId { get; set; }

    public int? OrderLineId { get; set; }

    public int? ProductVariantId { get; set; }

    public InvoiceDetailSourceType SourceType { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public decimal VatRate { get; set; }

    public decimal VatAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Note { get; set; }
}
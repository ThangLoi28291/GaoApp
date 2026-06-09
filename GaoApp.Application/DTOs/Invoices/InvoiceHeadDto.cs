namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceHeadDto
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerAddress { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public string? Note { get; set; }
    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public int? LockedByUserId { get; set; }

    public string? LockReason { get; set; }

    public List<InvoiceDetailDto> Details { get; set; } = new();
}
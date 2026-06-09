namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceListItemDto
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string? OrderNumber { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? BuyerName { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public int DetailCount { get; set; }

    public int AutoLineCount { get; set; }

    public int ManualLineCount { get; set; }
}
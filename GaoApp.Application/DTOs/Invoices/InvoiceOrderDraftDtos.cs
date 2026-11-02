namespace GaoApp.Application.DTOs.Invoices;

public sealed class InvoiceOrderDraftOrderDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public decimal GrandTotal { get; set; }
    public bool HasInvoice { get; set; }
}

public sealed record InvoiceOrderDraftReferenceDto(int Id, string? LegalEntityName);

public sealed class InvoiceOrderDraftResultDto
{
    public InvoiceOrderDraftOrderDto Order { get; set; } = new();
    public bool Created { get; set; }
    public List<InvoiceOrderDraftReferenceDto> Invoices { get; set; } = [];
}

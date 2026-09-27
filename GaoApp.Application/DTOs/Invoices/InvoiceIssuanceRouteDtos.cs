using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public sealed class InvoiceIssuanceRouteDto
{
    public int OrderId { get; set; }

    public InvoiceIssuanceRoute Route { get; set; }

    public DateTime? SelectedAtUtc { get; set; }

    public int? SelectedByUserId { get; set; }
}
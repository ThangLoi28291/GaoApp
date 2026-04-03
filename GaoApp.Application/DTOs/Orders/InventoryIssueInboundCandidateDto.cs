using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Orders;

public sealed class InventoryIssueInboundCandidateDto
{
    public int InventoryTransactionId { get; set; }
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }

    public InventoryReferenceType SourceReferenceType { get; set; }
    public int SourceReferenceId { get; set; }
    public int? SourceReferenceLineId { get; set; }

    public decimal QuantityChange { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}
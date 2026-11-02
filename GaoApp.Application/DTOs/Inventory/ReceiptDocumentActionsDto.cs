using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public sealed record ReceiptTitleProposalDto(long Id, string Title, string? RequestedBy, DateTime RequestedAtUtc);
public sealed record ReceiptDocumentActionsDto(int Id, string DocumentNo, string? DocumentTitle,
    int WarehouseId, PurchaseReceiptSource ReceiptSource, StockDocumentStatus Status, string RowVersion,
    bool NeverSubmitted, ReceiptTitleProposalDto? Proposal)
{
    public bool CanDelete { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierTaxCode { get; init; }
    public bool HasPurchaseOrder { get; init; }
    public bool IsMerchandisePaid { get; init; }
    public bool HasLinkedInvoice { get; init; }
    public bool CanCorrectSupplier { get; init; }
    public string? SupplierCorrectionBlockedReason { get; init; }
}

public class ReceiptVersionRequest
{
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class ReceiptTitleRequest : ReceiptVersionRequest
{
    [Required, StringLength(255)] public string Title { get; set; } = "";
}
public sealed class ReviewReceiptTitleRequest : ReceiptVersionRequest
{
    [Range(1, long.MaxValue)] public long RequestId { get; set; }
    public bool Approve { get; set; }
}
public sealed class CorrectReceiptSupplierRequest : ReceiptVersionRequest
{
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}

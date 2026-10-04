using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public sealed record ReceiptTitleProposalDto(long Id, string Title, string? RequestedBy, DateTime RequestedAtUtc);
public sealed record ReceiptDocumentActionsDto(int Id, string DocumentNo, string? DocumentTitle,
    int WarehouseId, PurchaseReceiptSource ReceiptSource, StockDocumentStatus Status, string RowVersion,
    bool NeverSubmitted, ReceiptTitleProposalDto? Proposal);

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

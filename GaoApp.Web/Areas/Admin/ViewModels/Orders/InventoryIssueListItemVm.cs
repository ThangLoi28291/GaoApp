using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public sealed class InventoryIssueListItemVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string ReasonType { get; set; } = string.Empty;

    public InventoryResolutionStatus Status { get; set; }
    public InventoryIssueSeverity Severity { get; set; }
    public bool IsOverdue { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }

    public int TotalLines { get; set; }
    public int ResolvedLines { get; set; }
    public int UnresolvedLines { get; set; }

    public string? InternalNotePreview { get; set; }
}
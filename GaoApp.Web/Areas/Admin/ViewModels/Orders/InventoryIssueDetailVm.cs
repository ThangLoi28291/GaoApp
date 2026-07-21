using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public sealed class InventoryIssueDetailVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;

    public InventoryResolutionStatus Status { get; set; }
    public InventoryIssueSeverity Severity { get; set; }
    public bool IsOverdue { get; set; }

    public string ReasonType { get; set; } = string.Empty;
    public DateTime OpenedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime? ReadyForApprovalAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }

    public string? InternalNote { get; set; }

    public int TotalLines { get; set; }
    public int ResolvedLines { get; set; }
    public int UnresolvedLines { get; set; }

    public List<InventoryIssueDetailLineVm> Lines { get; set; } = new();
    public List<InventoryIssueActionVm> Actions { get; set; } = new();
}





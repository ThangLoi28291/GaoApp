using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public sealed class InventoryIssueIndexVm
{
    public InventoryResolutionStatus? Status { get; set; }
    public bool? OnlyOverdue { get; set; }
    public string? Keyword { get; set; }

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ReadyCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int OverdueCount { get; set; }

    public List<InventoryIssueListItemVm> Items { get; set; } = new();
}

using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public sealed class InventoryIssueActionVm
{
    public int Id { get; set; }
    public InventoryIssueActionType ActionType { get; set; }
    public string? ActorName { get; set; }
    public DateTime ActionAtUtc { get; set; }

    public InventoryIssueReferenceType ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public int? OrderInventoryIssueLineId { get; set; }
    public string? Note { get; set; }
}
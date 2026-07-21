using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;

public class InventoryIssueActionRequestVm
{
    [Required]
    public int IssueId { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}
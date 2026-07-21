using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

public sealed class PurchaseRequestIndexViewModel
{
    public List<PurchaseRequestListItemDto> Items { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanViewStore { get; set; }
    public bool ShowingOnlyMine { get; set; }
}

public sealed class PurchaseRequestEditViewModel
{
    public SavePurchaseRequestRequest Request { get; set; } = new();
    public List<PurchaseRequestProductLookupDto> SelectedProducts { get; set; } = new();
    public List<string> UnitNames { get; set; } = new();
    public string? RequestNumber { get; set; }
    public string? WorkflowNote { get; set; }
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public bool CanSubmit { get; set; }
}

public sealed class PurchaseRequestDetailsViewModel
{
    public PurchaseRequestDetailDto Request { get; set; } = new();
    public bool IsOwner { get; set; }
    public bool CanEdit { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanReview { get; set; }
    public bool CanConvert { get; set; }
    public bool CanCancel { get; set; }
    public bool CanViewCost { get; set; }
    public bool CanViewPurchaseOrders { get; set; }
}

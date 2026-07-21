using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

public sealed class PurchaseOrderEditViewModel
{
    public SavePurchaseOrderRequest Request { get; set; } = new();
    public PurchaseOrderFormOptionsDto Options { get; set; } = new();
    public List<string> UnitNames { get; set; } = new();
    public string? OrderNumber { get; set; }
    public GaoApp.Domain.Enums.PurchaseOrderStatus Status { get; set; } = GaoApp.Domain.Enums.PurchaseOrderStatus.Draft;
}

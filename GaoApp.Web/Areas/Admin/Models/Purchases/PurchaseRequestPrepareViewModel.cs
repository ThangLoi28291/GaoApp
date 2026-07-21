using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

/// <summary>
/// Server-rendered state for the one-request/one-order conversion workspace.
/// SupplierLabel is display-only; ConvertAsync always validates SupplierId again.
/// </summary>
public sealed class PurchaseRequestPrepareViewModel
{
    public PurchaseRequestPreparationDto Preparation { get; set; } = new();
    public ConvertPurchaseRequestRequest Request { get; set; } = new();
    public string SupplierLabel { get; set; } = string.Empty;
}

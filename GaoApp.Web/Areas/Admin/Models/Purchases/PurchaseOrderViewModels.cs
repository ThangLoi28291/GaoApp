using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

public sealed class PurchaseOrderIndexViewModel
{
    public PurchaseOrderListResultDto Result { get; set; } = new();
    public List<PurchaseLookupOptionDto> LegalEntities { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanViewCost { get; set; }
    public bool CanApprove { get; set; }
}

public sealed class PurchaseOrderDetailsViewModel
{
    public PurchaseOrderDetailDto Order { get; set; } = new();
    public bool CanViewCost { get; set; }
    public bool CanEdit { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanReview { get; set; }
    public bool IsCreatedByCurrentUser { get; set; }
    public bool CanMarkSent { get; set; }
    public bool CanCancel { get; set; }
    public bool CanCreateReceipt { get; set; }
    public bool CanPrint { get; set; }
    public bool CanResolveItems { get; set; }
    public bool CanQuickCreateProduct { get; set; }
    public bool CanCreateUnit { get; set; }
    public ProcurementQuickCreateOptionsDto QuickCreateOptions { get; set; } = new();
}

public sealed class PurchaseOrderCommercialEditViewModel
{
    public PurchaseOrderDetailDto Order { get; set; } = new();
    public UpdateSourcePurchaseOrderCommercialRequest Request { get; set; } = new();
    public PurchaseOrderFormOptionsDto Options { get; set; } = new();
}

public sealed class PurchaseOrderPrintViewModel
{
    public PurchaseOrderDetailDto Order { get; set; } = new();
    public bool AutoPrint { get; set; }
}

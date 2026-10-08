using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.POS;

namespace GaoApp.Web.Areas.Admin.ViewModels.Delivery;

public sealed record DeliveryBillViewModel(DeliveryDetailDto Delivery, string StoreName, string TerminalName,
    string EmployeeName, string WarehouseName, string LookupUrl, string QrDataUrl, DeliveryPickingDetailDto? Picking = null,
    OrderReceiptDto? SourceReceipt = null);

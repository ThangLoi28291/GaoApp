using GaoApp.Web.Security;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/order-detail")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
public class POSOrderDetailController : BasePOSPageController
{
    private readonly IPOSService _pos;
    private readonly ILegalEntityReconciliationService _reconciliation;
    private readonly IAuthorizationService _authorization;

    public POSOrderDetailController(
        IPOSService pos,
        ILegalEntityReconciliationService reconciliation,
        IAuthorizationService authorization,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
        _reconciliation = reconciliation;
        _authorization = authorization;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Index(int orderId, CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);

        var receipt = await _pos.GetReceiptAsync(orderId, ct);
        ViewBag.CanCompleteReturnRestock = (await _authorization.AuthorizeAsync(User, PermissionCodes.Inventory.StockDocument.Approve)).Succeeded;
        var canReconcile = await _authorization.AuthorizeAsync(
            User,
            PermissionCodes.System.LegalEntity.Reconcile);
        if (canReconcile.Succeeded)
        {
            receipt.LegalEntityBreakdown = await _reconciliation
                .GetOrderBreakdownAsync(orderId, ct);
            var canManageInvoices = await _authorization.AuthorizeAsync(
                User,
                PermissionCodes.System.Integration.Manage);
            ViewBag.CanOpenLegalEntityInvoices = canManageInvoices.Succeeded;
        }

        ViewData["Title"] = $"Chi tiết đơn #{orderId}";
        return View(receipt);
    }
}

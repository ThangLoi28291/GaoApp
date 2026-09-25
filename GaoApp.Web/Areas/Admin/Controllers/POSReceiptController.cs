using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/receipt")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[Authorize(Policy = PermissionCodes.Pos.Order.Reprint)]
public class POSReceiptController : BasePOSPageController
{
    private readonly IPOSService _pos;

    public POSReceiptController(
        IPOSService pos,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Index(int orderId,
        [FromServices] GaoApp.Web.Services.Printing.ReceiptTemplateService templates,
        [FromServices] IPOSRuntimeContextAccessor runtime, CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);

        var receipt = await _pos.GetReceiptAsync(orderId, ct);
        ViewData["Title"] = $"Phiếu in #{orderId}";
        return View("~/Areas/Admin/Views/ReceiptTemplates/Print.cshtml",
            new GaoApp.Web.Services.Printing.ReceiptPrintModel(receipt, await templates.ListAsync(ct), CurrentStoreId, runtime.TerminalId, true));
    }
}

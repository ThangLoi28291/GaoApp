using GaoApp.Application.Common.Security;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/pos/printer-settings")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class POSPrinterSettingsController(ReceiptTemplateService templates, IPOSRuntimeContextAccessor runtime) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.StoreId = CurrentStoreId;
        ViewBag.TerminalId = runtime.TerminalId;
        ViewBag.TerminalName = runtime.TerminalName ?? "Thiết bị hiện tại";
        ViewBag.ReceiptDefault = await templates.GetDefaultAsync(ct);
        ViewBag.StoreInfo = await templates.GetStoreInfoAsync(ct);
        return View();
    }
}

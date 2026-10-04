using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/pos-shift/requests")]
public sealed class POSRequestsController(IStoreAdminAccess admin) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? tab, CancellationToken ct)
    {
        ViewBag.IsAdmin = await admin.IsAdminAsync(ct);
        ViewBag.ActiveTab = tab == "payment" ? "payment" : "cash";
        return View();
    }
}

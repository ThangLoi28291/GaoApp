using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Admin.StoreMonitorView)]
[Route("admin/store-monitor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StoreMonitorController(ITenantContext tenant, AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (tenant.IsHostAdmin || tenant.StoreId is not > 0) return Forbid();
        var name = await db.Stores.AsNoTracking().Where(x => x.Id == tenant.StoreId && x.IsActive && !x.IsDeleted)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (name is null) return Forbid();
        ViewData["StoreName"] = name;
        return View();
    }
}

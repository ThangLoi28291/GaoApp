using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/service-revenue"), Authorize(Policy = StorefrontPermissions.ViewSettings)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StorefrontServiceRevenueController(IStorefrontSettlementService service, AppDbContext db,
    ICurrentUser user, ICurrentStorePermissionService permissions) : BaseAdminController
{
    [HttpGet(""), HttpGet("/Admin/StorefrontFulfillment/ServiceSettings")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var setting = await service.GetServiceSettingAsync(ct); var store = CurrentStoreId;
        var owners = await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == store && x.IsActive).OrderBy(x => x.Name).Take(100).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct);
        var taxes = await db.Taxes.AsNoTracking().Where(x => x.StoreId == store && x.IsActive).OrderBy(x => x.Rate).Take(100).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct);
        return View(new StorefrontServiceSettingPage(setting, owners, taxes, await permissions.HasPermissionAsync(store, user.UserId!.Value, StorefrontPermissions.ManageSettings, ct)));
    }
    [HttpPost(""), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageSettings)]
    public async Task<IActionResult> Save([FromBody] StorefrontServiceRevenueSettingRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request is null) return BadRequest(new { message = "Kiểm tra tên, chủ thể và thuế dịch vụ." });
        return Json(await service.SaveServiceSettingAsync(request, ct));
    }
}

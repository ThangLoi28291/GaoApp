using GaoApp.Application.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/orders"), Authorize(Policy = StorefrontPermissions.ViewOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StorefrontOrdersController(IStorefrontOrderInboxService service,IStorefrontBankEvidenceReadService? bankEvidence=null) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 25, CancellationToken ct = default)
    { if (!ModelState.IsValid) return BadRequest("Trang không hợp lệ."); return View(await service.ListAsync(page, pageSize, ct)); }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await service.GetAsync(id, ct));
    [HttpGet("{id:int}/bank-evidence")]
    public async Task<IActionResult> BankEvidence(int id,CancellationToken ct)=>bankEvidence is null?NotFound():View(await bankEvidence.GetAsync(id,ct));
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(int afterId = 0, CancellationToken ct = default)
    { if (!ModelState.IsValid) return BadRequest("Mốc đơn không hợp lệ."); return Json(await service.NotificationsAsync(afterId, ct)); }
}

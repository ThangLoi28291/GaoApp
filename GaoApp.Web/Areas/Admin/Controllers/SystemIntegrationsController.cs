using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.SystemIntegrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed class SystemIntegrationForm
{
    public bool ZaloEnabled { get; set; }
    public bool AiEnabled { get; set; }
    public bool CampaignsEnabled { get; set; }
    public string Version { get; set; } = "";
}

[Area("Admin"), Route("admin/system/integrations")]
[Authorize(Policy = PermissionCodes.System.Setting.Update), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(2097152)]
public sealed class SystemIntegrationsController(SystemIntegrationSwitchStore switches, AppDbContext db, ICurrentUser user) : Controller
{
    private Task<bool> IsSystemAdmin(CancellationToken ct) => user.IsAuthenticated && user.UserId is > 0
        ? db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId && x.IsActive && !x.IsDeleted && x.IsHostAdmin, ct)
        : Task.FromResult(false);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await IsSystemAdmin(ct)) return Forbid();
        try { await LoadStores(ct); return View(switches.State()); }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Message); }
    }

    private async Task LoadStores(CancellationToken ct)
    {
        ViewData["IntegrationStores"] = await db.Stores.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name + (x.IsActive ? "" : " (ngừng hoạt động)"), x.Id.ToString())).ToListAsync(ct);
    }

    [HttpPost("connections")]
    public async Task<IActionResult> SaveConnections(SystemIntegrationConnectionsForm form, CancellationToken ct)
    {
        if (!await IsSystemAdmin(ct)) return Forbid();
        try
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Version) || form.Zalo is null || form.Zalo.Count > 50)
                throw AssistantException.Invalid();
            var existing = switches.State().Connections!.Zalo;
            var storeIds = form.Zalo.Where(c => c.StoreId > 0).Select(c => c.StoreId).Distinct().ToArray();
            var active = await db.Stores.AsNoTracking().Where(x => storeIds.Contains(x.Id) && x.IsActive && !x.IsDeleted)
                .Select(x => x.Id).ToArrayAsync(ct);
            if (form.Zalo.Any(c => c.StoreId > 0 && !active.Contains(c.StoreId) && !existing.Any(e => e.Id == c.Id && e.StoreId == c.StoreId)))
                throw new AssistantException(400, "integration_store_invalid", "Hãy chọn cửa hàng đang hoạt động cho kết nối Zalo.");
            await switches.SaveConnectionsAsync(form, user.UserId!.Value, ct);
            TempData["SystemIntegrationsSaved"] = "Đã lưu cấu hình kết nối. Thay đổi có hiệu lực ngay; khóa được lưu mã hóa.";
            return RedirectToAction(nameof(Index));
        }
        catch (AssistantException ex)
        {
            // Never let MVC retain submitted PEM/token values for redisplay or validation output.
            ModelState.Clear();
            ModelState.AddModelError("", ex.Message);
            Response.StatusCode = ex.Status;
            try
            {
                await LoadStores(ct);
                var state = switches.State();
                return View("Index", state with { Saved = state.Saved with { Version = form.Version } });
            }
            catch (AssistantException) { return StatusCode(ex.Status, ex.Message); }
        }
    }

    [HttpPost("")]
    public async Task<IActionResult> Save(SystemIntegrationForm form, CancellationToken ct)
    {
        if (!await IsSystemAdmin(ct)) return Forbid();
        try
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Version)) throw AssistantException.Invalid();
            await switches.SaveAsync(form.ZaloEnabled, form.AiEnabled, form.CampaignsEnabled, form.Version, user.UserId!.Value, ct);
            TempData["SystemIntegrationsSaved"] = "Đã cập nhật công tắc tích hợp toàn hệ thống.";
            return RedirectToAction(nameof(Index));
        }
        catch (AssistantException ex)
        {
            await LoadStores(ct);
            ModelState.AddModelError("", ex.Message);
            Response.StatusCode = ex.Status;
            try
            {
                var state = switches.State();
                return View("Index", state with { Saved = state.Saved with { Version = form.Version },
                    ZaloEnabled = form.ZaloEnabled, AiEnabled = form.AiEnabled, CampaignsEnabled = form.CampaignsEnabled });
            }
            catch (AssistantException) { return StatusCode(ex.Status, ex.Message); }
        }
    }
}

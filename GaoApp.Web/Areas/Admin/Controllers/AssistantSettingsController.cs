using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed class AssistantSettingsForm
{
    public bool Enabled { get; set; }
    public bool ChatEnabled { get; set; }
    public bool ReceivingEnabled { get; set; }
    public bool NormalizeEnabled { get; set; }
    public string Version { get; set; } = "";
}
public sealed record AssistantSettingsPage(AssistantStoreSwitches Switches, bool ConnectorEnabled);

[Area("Admin"), Route("admin/assistant/settings")]
[Authorize(Policy = PermissionCodes.System.Setting.Update), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantSettingsController(AssistantStoreSettings settings, ITenantContext tenant,
    ICurrentUser user, IOptionsMonitor<AssistantOptions> options) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (tenant.StoreId is not > 0) return Forbid();
        try { return View(new AssistantSettingsPage(await settings.ReadAsync(tenant.StoreId.Value, ct), options.CurrentValue.Enabled)); }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Message); }
    }
    [HttpPost("")]
    public async Task<IActionResult> Save(AssistantSettingsForm form, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0 || user.UserId is not > 0) return Forbid();
        try
        {
            if (!ModelState.IsValid || string.IsNullOrEmpty(form.Version)) throw AssistantException.Invalid();
            await settings.SaveAsync(tenant.StoreId.Value, user.UserId.Value, new(tenant.StoreId.Value,
                form.Enabled, form.ChatEnabled, form.ReceivingEnabled, form.NormalizeEnabled), form.Version, ct);
            TempData["AssistantSettingsSaved"] = "Đã lưu cấu hình AI cho cửa hàng hiện tại.";
            return RedirectToAction(nameof(Index));
        }
        catch (AssistantException ex)
        {
            ModelState.AddModelError("", ex.Message);
            Response.StatusCode = ex.Status;
            // Preserve the stale version after a conflict so a second click cannot overwrite the other change.
            return View("Index", new AssistantSettingsPage(new(tenant.StoreId.Value, form.Enabled, form.ChatEnabled,
                form.ReceivingEnabled, form.NormalizeEnabled, form.Version), options.CurrentValue.Enabled));
        }
    }
}

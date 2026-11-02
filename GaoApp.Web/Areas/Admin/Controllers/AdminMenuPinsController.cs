using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using GaoApp.Web.Services;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/menu-pins")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminMenuPinsController(IAdminMenuPinService service, AdminNavigationProvider navigation) : BaseAdminController
{
    [HttpGet("")]
    [Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(await service.GetWorkspaceAsync(CurrentStoreId, CurrentUserId, ct));
    }

    [HttpGet("configured")]
    public async Task<IActionResult> Configured(CancellationToken ct)
    {
        var view = new ViewContext { HttpContext = HttpContext, RouteData = RouteData, ActionDescriptor = ControllerContext.ActionDescriptor };
        var configured = await navigation.GetPinsAsync(view);
        var defaults = await navigation.GetMobileDefaultsAsync(view);
        return Json(new
        {
            MenuIds = configured.Select(menu => menu.Id),
            DefaultTabs = defaults.Select(tab => new { Id = tab.Menu.Id, tab.Key, tab.Label })
        });
    }

    [HttpGet("workspace")]
    [Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
    public async Task<IActionResult> Workspace(CancellationToken ct) => Json(await service.GetWorkspaceAsync(CurrentStoreId, CurrentUserId, ct));

    [HttpGet("preview")]
    [Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
    public async Task<IActionResult> Preview(string type, int id, [FromQuery] List<int> menuIds, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Đối tượng hoặc danh sách menu không hợp lệ." });
        try { return Json(await service.PreviewAsync(CurrentStoreId, CurrentUserId, type ?? "", id, menuIds, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("save"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
    public async Task<IActionResult> Save([FromBody] SaveAdminMenuPinsRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Chọn tối đa 4 mục và tải lại cấu hình hợp lệ trước khi lưu." });
        try { return Json(await service.SaveAsync(CurrentStoreId, CurrentUserId, request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

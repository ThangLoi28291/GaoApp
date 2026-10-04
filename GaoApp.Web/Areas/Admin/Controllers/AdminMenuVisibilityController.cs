using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
public sealed class AdminMenuVisibilityController(IAdminMenuVisibilityService service) : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await service.GetSubjectsAsync(CurrentStoreId, ct));

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Detail(string type, int id, CancellationToken ct)
    {
        try { return Json(await service.GetAsync(CurrentStoreId, type, id, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveMenuVisibilityRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Dữ liệu không hợp lệ. Hãy tải lại trang rồi thử lại." });
        try { return Json(await service.SaveAsync(CurrentStoreId, request, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

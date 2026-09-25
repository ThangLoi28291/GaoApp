using GaoApp.Application.Common.Security;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Display;
using GaoApp.Application.Interfaces.Services.Display;
using GaoApp.Web.Hubs;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class DisplayPromotionController : Controller
{
    private readonly IDisplayPromotionService _service;
    private readonly GaoApp.Application.Common.Abstractions.IFileStorageService _storage;
    private readonly IHubContext<PosHub> _hubContext;
    private readonly ITenantContext _tenantContext;

    public DisplayPromotionController(
        IDisplayPromotionService service,
        GaoApp.Application.Common.Abstractions.IFileStorageService storage,
        IHubContext<PosHub> hubContext,
        ITenantContext tenantContext)
    {
        _service = service;
        _storage = storage;
        _hubContext = hubContext;
        _tenantContext = tenantContext;
    }
    private async Task BroadcastPromotionChangedAsync(string eventType = "customer_display_promotion_changed")
    {
        var storeId = _tenantContext.StoreId
            ?? throw new InvalidOperationException("Không xác định được StoreId hiện tại.");

        await _hubContext.Clients.Group($"store:{storeId}").SendAsync("pos:event", new
        {
            eventType,
            payload = new
            {
                reason = "display_promotion_changed",
                at = DateTime.UtcNow
            }
        });
    }
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.View)]
    public async Task<IActionResult> Index(
        [FromServices] GaoApp.Web.Services.CustomerDisplayService display,
        [FromServices] IAuthorizationService authorization,
        CancellationToken ct)
    {
        var items = await _service.GetListAsync(ct);
        ViewBag.GuestWifi = await display.GetWifiAsync(ct);
        ViewBag.CanManageWifi = (await authorization.AuthorizeAsync(User, PermissionCodes.Catalog.DisplayPromotion.Manage)).Succeeded;

        return View(items);
    }

    [HttpPut]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.Manage)]
    public async Task<IActionResult> SaveWifi([FromBody] GaoApp.Web.Services.SaveGuestWifiRequest request,
        [FromServices] GaoApp.Web.Services.CustomerDisplayService display, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await display.SaveWifiAsync(request, ct);
        await BroadcastPromotionChangedAsync("customer_display_info_changed");
        return Ok(result);
    }
    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.View)]
    public async Task<IActionResult> Detail(
    int id,
    CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(id, ct);

        if (item == null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy promotion."
            });
        }

        return Json(new
        {
            success = true,
            data = item
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.Manage)]
    public async Task<IActionResult> ToggleActive(
        int id,
        bool isActive,
        CancellationToken ct)
    {
        await _service.SetActiveAsync(id, isActive, ct);
        await BroadcastPromotionChangedAsync();
        return Json(new
        {
            success = true
        });
    }
    [HttpGet]
    [RequireAnyPermission(PermissionCodes.Pos.Order.View, PermissionCodes.Catalog.DisplayPromotion.View)]
    public async Task<IActionResult> ActiveForCustomerDisplay(CancellationToken ct)
    {
        var items = await _service.GetActiveForCustomerDisplayAsync(ct);

        return Json(new
        {
            success = true,
            data = items
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.Manage)]
    [RequestSizeLimit(64 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        UpsertDisplayPromotionDto dto,
        IFormFile? mediaFile,
        CancellationToken ct)
    {
        if (mediaFile != null)
        {
            var uploadError = await UploadSecurityValidator
                .ValidateDisplayMediaAsync(mediaFile, ct);

            if (uploadError != null)
                return BadRequest(new { success = false, message = uploadError });

            dto.MediaUrl = await SaveMediaAsync(mediaFile, ct);
        }

        var id = await _service.CreateAsync(dto, ct);
        await BroadcastPromotionChangedAsync();
        return Json(new
        {
            success = true,
            id
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.Manage)]
    [RequestSizeLimit(64 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
    public async Task<IActionResult> Update(
        int id,
        UpsertDisplayPromotionDto dto,
        IFormFile? mediaFile,
        CancellationToken ct)
    {
        if (mediaFile != null)
        {
            var uploadError = await UploadSecurityValidator
                .ValidateDisplayMediaAsync(mediaFile, ct);

            if (uploadError != null)
                return BadRequest(new { success = false, message = uploadError });

            dto.MediaUrl = await SaveMediaAsync(mediaFile, ct);
        }

        await _service.UpdateAsync(id, dto, ct);
        await BroadcastPromotionChangedAsync();
        return Json(new
        {
            success = true
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Catalog.DisplayPromotion.Manage)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        await BroadcastPromotionChangedAsync();
        return Json(new
        {
            success = true
        });
    }

    private async Task<string> SaveMediaAsync(
        IFormFile file,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);

        var fileName =
            $"{Guid.NewGuid():N}{ext}";

        await using var stream = file.OpenReadStream();
        await _storage.SaveAsync(stream, $"uploads/display/{fileName}", ct);

        return $"/uploads/display/{fileName}";
    }
}

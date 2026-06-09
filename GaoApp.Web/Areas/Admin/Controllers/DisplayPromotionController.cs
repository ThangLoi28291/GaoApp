using GaoApp.Application.DTOs.Display;
using GaoApp.Application.Interfaces.Services.Display;
using GaoApp.Web.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DisplayPromotionController : Controller
{
    private readonly IDisplayPromotionService _service;
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<PosHub> _hubContext;

    public DisplayPromotionController(
        IDisplayPromotionService service,
        IWebHostEnvironment env,
        IHubContext<PosHub> hubContext)
    {
        _service = service;
        _env = env;
        _hubContext = hubContext;
    }
    private async Task BroadcastPromotionChangedAsync()
    {
        await _hubContext.Clients.All.SendAsync("pos:event", new
        {
            eventType = "customer_display_promotion_changed",
            payload = new
            {
                reason = "display_promotion_changed",
                at = DateTime.UtcNow
            }
        });
    }
    public async Task<IActionResult> Index(
        CancellationToken ct)
    {
        var items = await _service.GetListAsync(ct);

        return View(items);
    }
    [HttpGet]
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
    public async Task<IActionResult> Create(
        UpsertDisplayPromotionDto dto,
        IFormFile? mediaFile,
        CancellationToken ct)
    {
        if (mediaFile != null)
        {
            dto.MediaUrl = await SaveMediaAsync(mediaFile);
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
    public async Task<IActionResult> Update(
        int id,
        UpsertDisplayPromotionDto dto,
        IFormFile? mediaFile,
        CancellationToken ct)
    {
        if (mediaFile != null)
        {
            dto.MediaUrl = await SaveMediaAsync(mediaFile);
        }

        await _service.UpdateAsync(id, dto, ct);
        await BroadcastPromotionChangedAsync();
        return Json(new
        {
            success = true
        });
    }

    [HttpPost]
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

    private async Task<string> SaveMediaAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName);

        var fileName =
            $"{Guid.NewGuid():N}{ext}";

        var folder =
            Path.Combine(
                _env.WebRootPath,
                "uploads",
                "display");

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, fileName);

        await using var stream =
            new FileStream(path, FileMode.Create);

        await file.CopyToAsync(stream);

        return $"/uploads/display/{fileName}";
    }
}
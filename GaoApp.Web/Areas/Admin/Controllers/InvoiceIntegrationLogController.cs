using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class InvoiceIntegrationLogController : Controller
{
    private readonly IInvoiceIntegrationLogService _logService;
    private readonly IInvoiceIntegrationLogCleanupService _cleanupService;

    public InvoiceIntegrationLogController(
       IInvoiceIntegrationLogService logService,
       IInvoiceIntegrationLogCleanupService cleanupService)
    {
        _logService = logService;
        _cleanupService = cleanupService;
    }
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] InvoiceIntegrationLogQueryDto query,
        CancellationToken ct)
    {
        if (query.Page <= 0)
            query.Page = 1;

        if (query.PageSize <= 0)
            query.PageSize = 20;

        var result = await _logService.GetLogsAsync(query, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Không tải được danh sách log tích hợp.";

            return View(new InvoiceIntegrationLogIndexViewModel
            {
                Query = query
            });
        }

        return View(new InvoiceIntegrationLogIndexViewModel
        {
            Query = query,
            Result = result.Value
        });
    }

    [HttpGet]
    public async Task<IActionResult> Detail(
        int id,
        CancellationToken ct)
    {
        var result = await _logService.GetDetailAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Không tìm thấy log tích hợp.";
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }
    [HttpGet]
    public async Task<IActionResult> Cleanup(CancellationToken ct)
    {
        var request = new InvoiceIntegrationLogCleanupRequestDto
        {
            DryRun = true
        };

        var result = await _cleanupService.CleanupAsync(request, ct);

        return View(new InvoiceIntegrationLogCleanupViewModel
        {
            Request = request,
            Result = result.IsSuccess ? result.Value : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cleanup(
        InvoiceIntegrationLogCleanupRequestDto request,
        string submitMode,
        CancellationToken ct)
    {
        request.DryRun = !string.Equals(
            submitMode,
            "execute",
            StringComparison.OrdinalIgnoreCase);

        var result = await _cleanupService.CleanupAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Dọn log thất bại.";

            return View(new InvoiceIntegrationLogCleanupViewModel
            {
                Request = request
            });
        }

        TempData["Success"] = request.DryRun
            ? "Đã xem trước số log có thể dọn."
            : $"Đã dọn {result.Value.DeletedCount:N0} log.";

        return View(new InvoiceIntegrationLogCleanupViewModel
        {
            Request = request,
            Result = result.Value
        });
    }
}
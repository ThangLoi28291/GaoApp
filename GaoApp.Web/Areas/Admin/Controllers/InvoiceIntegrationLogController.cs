using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using GaoApp.Web.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class InvoiceIntegrationLogController : Controller
{
    private readonly IInvoiceIntegrationLogService _logService;

    public InvoiceIntegrationLogController(
        IInvoiceIntegrationLogService logService)
    {
        _logService = logService;
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
}
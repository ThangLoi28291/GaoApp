using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class ViettelInvoiceSyncController : Controller
{
    private readonly IViettelInvoiceListSyncService _syncService;

    public ViettelInvoiceSyncController(
        IViettelInvoiceListSyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new ViettelInvoiceSyncIndexViewModel
        {
            Query = new ViettelInvoiceListSyncRequestDto
            {
                FromDate = DateTime.Today,
                ToDate = DateTime.Today,
                PageSize = 100,
                UpdateLocalInvoices = true
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        ViettelInvoiceListSyncRequestDto query,
        CancellationToken ct)
    {
        var result = await _syncService.SyncAsync(query, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Đồng bộ danh sách Viettel thất bại.";

            return View(new ViettelInvoiceSyncIndexViewModel
            {
                Query = query
            });
        }

        TempData["Success"] = "Đồng bộ danh sách hóa đơn Viettel hoàn tất.";

        return View(new ViettelInvoiceSyncIndexViewModel
        {
            Query = query,
            Result = result.Value
        });
    }
}
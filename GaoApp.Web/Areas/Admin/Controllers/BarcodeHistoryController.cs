using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Web.Areas.Admin.ViewModels.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/barcode-history")]
public sealed class BarcodeHistoryController : Controller
{
    private readonly IBarcodeHistoryService _barcodeHistoryService;

    public BarcodeHistoryController(IBarcodeHistoryService barcodeHistoryService)
    {
        _barcodeHistoryService = barcodeHistoryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] BarcodeHistoryQueryRequest request, CancellationToken ct)
    {
        ViewData["Title"] = "Lịch sử barcode";

        var vm = new BarcodeHistoryIndexVm
        {
            Filter = request,
            Result = await _barcodeHistoryService.GetPagedAsync(request, ct)
        };

        return View(vm);
    }
}
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/stock-counts")]
[Authorize(Policy = PermissionCodes.Inventory.StockCount.View)]
public class StockCountPagesController : Controller
{
    private readonly IStockCountIndexReadService _indexReadService;

    public StockCountPagesController(IStockCountIndexReadService indexReadService)
    {
        _indexReadService = indexReadService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Phiếu kiểm kê kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        ViewData["Title"] = "Chi tiết phiếu kiểm kê";
        ViewBag.StockCountDocumentId = id;
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] StockCountIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _indexReadService.GetPageAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("warehouse-options")]
    public async Task<IActionResult> GetWarehouseOptions(CancellationToken ct)
        => Ok(await _indexReadService.GetWarehouseOptionsAsync(ct));

    [HttpGet("quick-view")]
    public async Task<IActionResult> GetQuickView(
        [FromQuery] int documentId,
        CancellationToken ct)
    {
        if (documentId <= 0)
            return BadRequest(new { message = "Phiếu kiểm kê cần xem không hợp lệ." });

        var result = await _indexReadService.GetQuickViewAsync(documentId, ct);

        return result is null
            ? NotFound(new { message = "Không tìm thấy phiếu kiểm kê." })
            : Ok(result);
    }
}

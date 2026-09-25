using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/stock-transfers")]
[Authorize(Policy = PermissionCodes.Inventory.StockTransfer.View)]
public class StockTransferController : Controller
{
    private readonly IStockTransferIndexReadService _indexReadService;

    public StockTransferController(IStockTransferIndexReadService indexReadService)
    {
        _indexReadService = indexReadService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Chuyển kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        ViewData["Title"] = "Chi tiết phiếu chuyển kho";
        ViewBag.StockTransferId = id;
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] StockTransferIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _indexReadService.GetPageAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("quick-view")]
    public async Task<IActionResult> GetQuickView(
        [FromQuery] int documentId,
        CancellationToken ct)
    {
        if (documentId <= 0)
            return BadRequest(new { message = "Phiếu chuyển kho cần xem không hợp lệ." });

        var result = await _indexReadService.GetQuickViewAsync(documentId, ct);

        return result is null
            ? NotFound(new { message = "Không tìm thấy phiếu chuyển kho." })
            : Ok(result);
    }
}

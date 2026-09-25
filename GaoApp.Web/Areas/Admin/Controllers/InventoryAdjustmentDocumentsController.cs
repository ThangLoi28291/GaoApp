using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-adjustment-documents")]

[Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
public class InventoryAdjustmentDocumentsController : Controller
{
    private readonly IInventoryAdjustmentIndexReadService _indexReadService;

    public InventoryAdjustmentDocumentsController(
        IInventoryAdjustmentIndexReadService indexReadService)
    {
        _indexReadService = indexReadService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Phiếu điều chỉnh kho";
        return View();
    }

    [HttpGet("create")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Create)]
    public IActionResult Create()
    {
        ViewData["Title"] = "Tạo phiếu điều chỉnh kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        ViewData["Title"] = "Chi tiết phiếu điều chỉnh kho";
        ViewBag.Id = id;
        return View();
    }

    [HttpGet("{id:int}/edit")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Update)]
    public IActionResult Edit(int id)
    {
        ViewData["Title"] = "Sửa phiếu điều chỉnh kho";
        ViewBag.Id = id;
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] InventoryAdjustmentIndexQueryRequest request,
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
            return BadRequest(new { message = "Phiếu điều chỉnh cần xem không hợp lệ." });

        var result = await _indexReadService.GetQuickViewAsync(documentId, ct);

        return result is null
            ? NotFound(new { message = "Không tìm thấy phiếu điều chỉnh kho." })
            : Ok(result);
    }
}

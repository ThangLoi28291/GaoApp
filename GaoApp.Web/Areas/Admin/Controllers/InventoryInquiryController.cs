using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-inquiry")]
[Authorize]
[Authorize(Policy = PermissionCodes.Inventory.Balance.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InventoryInquiryController : Controller
{
    private readonly IInventoryInquiryReadService _readService;

    public InventoryInquiryController(IInventoryInquiryReadService readService)
    {
        _readService = readService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Tra cứu tồn kho";
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] InventoryInquiryQueryRequest request,
        CancellationToken ct)
    {
        var result = await _readService.GetPageAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("quick-view")]
    public async Task<IActionResult> GetQuickView(
        [FromQuery] int warehouseId,
        [FromQuery] int productVariantId,
        CancellationToken ct)
    {
        if (warehouseId <= 0 || productVariantId <= 0)
            return BadRequest(new { message = "Dữ liệu xem nhanh không hợp lệ." });

        var result = await _readService.GetQuickViewAsync(
            warehouseId,
            productVariantId,
            ct);

        return result is null
            ? NotFound(new { message = "Không tìm thấy dữ liệu tồn kho." })
            : Ok(result);
    }
}

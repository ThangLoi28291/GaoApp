using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Interfaces.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/returns")]
[Authorize]
public class POSReturnController : Controller
{
    private readonly ISalesReturnService _salesReturnService;

    public POSReturnController(ISalesReturnService salesReturnService)
    {
        _salesReturnService = salesReturnService;
    }

    [HttpGet("order/{orderId:int}/eligibility")]
    public async Task<IActionResult> GetEligibility(int orderId, CancellationToken ct)
    {
        var result = await _salesReturnService.GetEligibilityAsync(orderId, ct);
        return Ok(result);
    }

    [HttpGet("order/{orderId:int}/history")]
    public async Task<IActionResult> GetHistory(int orderId, CancellationToken ct)
    {
        var result = await _salesReturnService.GetByOrderIdAsync(orderId, ct);
        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateSalesReturnRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _salesReturnService.CreateAsync(request, ct);
            return Ok(new
            {
                success = true,
                message = "Đã tạo phiếu trả hàng / hoàn tiền thành công.",
                data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }
}
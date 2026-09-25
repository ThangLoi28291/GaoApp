using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/invoice-input-stock")]
[Authorize]
[Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
public sealed class InvoiceInputStockController(InvoiceInputStockReadService service) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("data")]
    public async Task<IActionResult> Data([FromQuery] InvoiceInputStockQuery query, CancellationToken ct)
    {
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value.Date > query.ToDate.Value.Date)
            return BadRequest(new { message = "Từ ngày không được lớn hơn đến ngày." });
        if (query.ToDate?.Year >= 9999 || query.FromDate?.Year <= 1 || query.ToDate?.Year <= 1)
            return BadRequest(new { message = "Ngày lọc không hợp lệ." });
        return Ok(await service.GetPageAsync(query, ct));
    }
}

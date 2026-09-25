using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Interfaces.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports/cost-adjustments")]
[Authorize(Policy = PermissionCodes.Report.Profit.View)]
public sealed class CostAdjustmentReportController(IProfitReportReadService service) : BaseAdminController
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("data")]
    public async Task<IActionResult> GetData([FromQuery] ProfitReportQueryDto query, CancellationToken ct)
    {
        try { return Ok(await service.ReadAsync(query, true, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

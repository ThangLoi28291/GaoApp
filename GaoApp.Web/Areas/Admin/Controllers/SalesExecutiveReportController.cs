using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports/overview")]
[Authorize(Policy = PermissionCodes.Report.Sales.View)]
public sealed class SalesExecutiveReportController : BaseAdminController
{
    private readonly ISalesReportReadService _readService;

    public SalesExecutiveReportController(ISalesReportReadService readService)
    {
        _readService = readService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Tổng quan kinh doanh";
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] SalesExecutiveDashboardQueryDto query,
        CancellationToken ct)
    {
        try
        {
            var result = await _readService.GetExecutiveDashboardAsync(query, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

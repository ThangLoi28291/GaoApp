using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Application.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports/expenses")]
[Authorize(Policy = PermissionCodes.Report.Profit.View)]
public sealed class OperatingExpenseController(IOperatingExpenseService expenses, SalesReportingPeriodPolicy periods) : BaseAdminController
{
    [HttpGet("")]
    public IActionResult Index() {
        ViewData["ReportView"] = "expenses"; ViewData["Title"] = "Chi phí vận hành";
        return View("~/Areas/Admin/Views/ManagementReport/Workspace.cshtml");
    }
    [HttpGet("data")]
    public Task<IActionResult> GetData(DateTime? fromDate, DateTime? toDate, string? search, string? status, int page = 1, CancellationToken ct = default)
        => Execute(async () => {
            var p = periods.Resolve(new SalesExecutiveDashboardQueryDto { FromDate = fromDate, ToDate = toDate, Compare = "none" }, DateTime.UtcNow).Current.Period;
            return await expenses.ListAsync(p.FromDate, p.ToDate, search, status, page, ct);
        });
    [HttpPost("")]
    [Authorize(Policy = PermissionCodes.Report.Expenses.Manage)]
    public Task<IActionResult> Create([FromBody] ExpenseWriteDto request, CancellationToken ct)
        => Execute(() => expenses.CreateAsync(request, ct));
    [HttpPost("{id:int}/edit")]
    [Authorize(Policy = PermissionCodes.Report.Expenses.Manage)]
    public Task<IActionResult> Edit(int id, [FromBody] ExpenseWriteDto request, CancellationToken ct)
        => Execute(() => expenses.UpdateAsync(id, request, ct));
    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = PermissionCodes.Report.Expenses.Confirm)]
    public Task<IActionResult> Confirm(int id, [FromBody] ExpenseActionDto request, CancellationToken ct)
        => Execute(() => expenses.TransitionAsync(id, "confirm", request, ct));
    [HttpPost("{id:int}/void")]
    [Authorize(Policy = PermissionCodes.Report.Expenses.Confirm)]
    public Task<IActionResult> Void(int id, [FromBody] ExpenseActionDto request, CancellationToken ct)
        => Execute(() => expenses.TransitionAsync(id, "void", request, ct));
    private async Task<IActionResult> Execute<T>(Func<Task<T>> work) {
        if (!ModelState.IsValid) return BadRequest(new { message = "Dữ liệu hoặc bộ lọc chi phí không hợp lệ." });
        try { return Ok(await work()); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}

using System.Globalization;
using System.Text;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Interfaces.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports")]
[Authorize(Policy = PermissionCodes.Report.Profit.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationsReportController(IOperationsReportService reports, ITreasuryService treasury) : BaseAdminController
{
    [HttpGet("cashflow")]
    public IActionResult CashFlow() => Page("cashflow", "Thu chi & dòng tiền");
    [HttpGet("inventory")]
    public IActionResult Inventory() => Page("inventory", "Tồn kho & luân chuyển");
    [HttpGet("debts")]
    public IActionResult Debts() => Page("debts", "Công nợ & tuổi nợ");
    private IActionResult Page(string section, string title) {
        ViewData["ReportView"] = section; ViewData["Title"] = title;
        return View("~/Areas/Admin/Views/OperationsReport/Workspace.cshtml");
    }
    [HttpGet("operations/{section}/data")]
    public async Task<IActionResult> Data(string section, [FromQuery] OperationsReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc không hợp lệ." });
        try { return section switch {
            "cashflow" => Ok(await reports.CashFlowAsync(query, ct)),
            "inventory" => Ok(await reports.StockAsync(query, ct)),
            "debts" => Ok(await reports.DebtAsync(query, ct)),
            _ => NotFound()
        }; } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
    [HttpGet("operations/{section}/export")]
    [Authorize(Policy = PermissionCodes.Report.Profit.Export)]
    public async Task<IActionResult> Export(string section, [FromQuery] OperationsReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc không hợp lệ." });
        var text = new StringBuilder();
        string Number(decimal? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";
        string Cell(string v) {
            if (v.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' && !decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
        void Row(params string[] values) => text.AppendLine(string.Join(",", values.Select(Cell)));
        try {
            if (section == "cashflow") {
                var report = await reports.CashFlowAsync(query, ct);
                Row("Từ ngày", report.Period.FromDate.ToString("yyyy-MM-dd"), "Đến ngày", report.Period.ToDate.ToString("yyyy-MM-dd"));
                foreach (var warning in report.Warnings) Row("Lưu ý", warning);
                Row("Quỹ", "Đầu kỳ", "Thu", "Chi", "Thuần", "Cuối kỳ");
                foreach (var x in report.Summary) Row(x.Name, Number(x.Opening), Number(x.Inflow), Number(x.Outflow), Number(x.Net), Number(x.Closing));
                Row("Mã", "Thời điểm UTC+7", "Quỹ", "Nội dung", "Nguồn", "Số tiền có dấu", "Tham chiếu", "Chuyển quỹ");
                foreach (var x in report.Items) Row(x.Id, x.AtUtc.AddHours(7).ToString("yyyy-MM-dd HH:mm"), x.Fund, x.Name, x.Source, Number(x.Amount), x.Reference ?? "", x.IsTransfer ? "Có" : "");
            } else if (section == "inventory") {
                var report = await reports.StockAsync(query, ct);
                Row("Từ ngày", report.Period.FromDate.ToString("yyyy-MM-dd"), "Đến ngày", report.Period.ToDate.ToString("yyyy-MM-dd"));
                foreach (var warning in report.Warnings) Row("Lưu ý", warning);
                Row("Kho", "Sản phẩm", "SKU", "Đơn vị gốc", "Đầu kỳ", "Nhập", "Xuất", "Cuối kỳ", "Giá trị cuối kỳ", "Xuất bán", "Số ngày đủ bán", "Trạng thái", "Giá vốn tạm", "Thiếu dữ liệu");
                foreach (var x in report.Items) Row(x.Warehouse, x.Name, x.Sku, x.Unit, Number(x.Opening), Number(x.Inbound), Number(x.Outbound), Number(x.Closing), Number(x.ClosingValue), Number(x.Sold), Number(x.DaysCover), x.State, x.IsProvisional ? "Có" : "", x.IsIncomplete ? "Có" : "");
            } else if (section == "debts") {
                var report = await reports.DebtAsync(query, ct);
                Row("Từ ngày", report.Period.FromDate.ToString("yyyy-MM-dd"), "Đến ngày", report.Period.ToDate.ToString("yyyy-MM-dd"));
                foreach (var warning in report.Warnings) Row("Lưu ý", warning);
                Row("Loại", "Đối tượng", "Chứng từ", "Hạn thanh toán", "Đầu kỳ", "Tăng nợ", "Giảm nợ", "Cuối kỳ", "Tuổi nợ");
                foreach (var x in report.Items) Row(x.Kind == "receivable" ? "Phải thu" : "Phải trả", x.Party, x.Document, x.DueDate?.ToString("yyyy-MM-dd") ?? "", Number(x.Opening), Number(x.Increase), Number(x.Decrease), Number(x.Closing), x.Aging);
            } else return NotFound();
            var encoding = new UTF8Encoding(true);
            return File(encoding.GetPreamble().Concat(encoding.GetBytes(text.ToString())).ToArray(), "text/csv; charset=utf-8", $"bao-cao-{section}.csv");
        } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
    [HttpGet("treasury/sources")]
    [Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public async Task<IActionResult> Sources(CancellationToken ct, int? expenseId = null, int? payableId = null) {
        if (!ModelState.IsValid) return BadRequest(new { message = "Mã chứng từ không hợp lệ." });
        try { return Ok(await treasury.SourcesAsync(ct, expenseId, payableId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpPost("treasury/entries")]
    [Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public Task<IActionResult> Create([FromBody] TreasuryWriteDto input, CancellationToken ct) => Write(async () => await treasury.CreateAsync(input, ct));
    [HttpPost("treasury/entries/{id:int}/reverse")]
    [Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public Task<IActionResult> Reverse(int id, [FromBody] TreasuryReverseDto input, CancellationToken ct) => Write(async () => await treasury.ReverseAsync(id, input, ct));
    [HttpPost("treasury/opening")]
    [Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public Task<IActionResult> Opening([FromBody] TreasuryOpeningDto input, CancellationToken ct) => Write(async () => { await treasury.OpeningAsync(input, ct); return 0; });
    [HttpPost("treasury/payables/{id:int}/due-date")]
    [Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public Task<IActionResult> DueDate(int id, [FromBody] PayableDueDateDto input, CancellationToken ct) => Write(async () => { await treasury.SetDueDateAsync(id, input, ct); return id; });
    private async Task<IActionResult> Write(Func<Task<int>> action) {
        if (!ModelState.IsValid) return BadRequest(new { message = "Kiểm tra thông tin phiếu." });
        try { return Ok(new { id = await action() }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}

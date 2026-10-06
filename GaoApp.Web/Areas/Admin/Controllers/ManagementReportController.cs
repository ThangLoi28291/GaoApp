using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Interfaces.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports")]
[Authorize(Policy = PermissionCodes.Report.Profit.View)]
public sealed class ManagementReportController(IManagementReportService reports) : BaseAdminController
{
    [HttpGet("products")]
    public IActionResult Products() => Page("products", "Phân tích sản phẩm");
    [HttpGet("categories")]
    public IActionResult Categories() => Page("categories", "Nhóm sản phẩm");
    [HttpGet("customers")]
    public IActionResult Customers() => Page("customers", "Phân tích khách hàng");
    [HttpGet("groups")]
    public IActionResult Groups() => Page("groups", "Khách sỉ & khách lẻ");
    private IActionResult Page(string section, string title) {
        ViewData["ReportView"] = section; ViewData["Title"] = title;
        return View("Workspace");
    }
    [HttpGet("management/data")]
    public async Task<IActionResult> GetData([FromQuery] ManagementReportQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc báo cáo không hợp lệ." });
        try { return Ok(await reports.ReadAsync(query, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpGet("management/export")]
    [Authorize(Policy = PermissionCodes.Report.Profit.Export)]
    public async Task<IActionResult> Export([FromQuery] ManagementReportQueryDto query, string dimension = "profit", CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc báo cáo không hợp lệ." });
        if (dimension is not ("profit" or "products" or "categories" or "customers" or "groups")) return BadRequest();
        try {
            var result = await reports.ReadAsync(query, ct);
            var text = new System.Text.StringBuilder();
            string Number(decimal? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            void Row(params string[] values) => text.AppendLine(string.Join(",", values.Select(value => {
                // Spreadsheet formula injection also applies to store-entered names and SKUs.
                if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' &&
                    !decimal.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            })));
            Row("Kỳ báo cáo", result.Period.FromDate.ToString("yyyy-MM-dd"), result.Period.ToDate.ToString("yyyy-MM-dd"));
            if (dimension == "profit") {
                Row("Chỉ tiêu", "Số tiền VND", "Trạng thái");
                var c = result.Current;
                Row("Doanh thu thuần", Number(c.Summary.NetSales.Value), c.Summary.NetSales.Quality.ToString());
                Row("Giá vốn", Number(c.Summary.Cogs.Value), c.Summary.CostState);
                Row("Lợi nhuận gộp", Number(c.Summary.GrossProfit.Value), c.Summary.CostState);
                Row("Chi phí vận hành toàn cửa hàng", Number(c.OperatingExpenses), "Đã ghi nhận");
                Row("LN sau chi phí ghi nhận", Number(c.OperatingProfit), c.Summary.CostState);
            } else {
                var rows = dimension switch { "products" => result.Current.Products, "categories" => result.Current.Categories,
                    "customers" => result.Current.Customers, _ => result.Current.CustomerGroups };
                List<string> headers = ["Tên", "Nhóm", "SKU", "Đơn bán"];
                if (dimension == "products") headers.Add("SL đơn vị gốc");
                headers.AddRange(["Doanh thu thuần", "Giá vốn", "LN gộp", "Biên LN %", "Trạng thái giá vốn"]);
                Row(headers.ToArray());
                foreach (var x in rows) {
                    List<string> values = [x.Name, x.Group ?? "", x.Sku ?? "", x.SalesOrders.ToString()];
                    // Quantities from different products may have different base units.
                    if (dimension == "products") values.Add(Number(x.BaseQuantity));
                    values.AddRange([Number(x.Summary.NetSales.Value), Number(x.Summary.Cogs.Value), Number(x.Summary.GrossProfit.Value), Number(x.Summary.GrossMargin.Value), x.Summary.CostState]);
                    Row(values.ToArray());
                }
            }
            var encoding = new System.Text.UTF8Encoding(true);
            return File(encoding.GetPreamble().Concat(encoding.GetBytes(text.ToString())).ToArray(), "text/csv; charset=utf-8", "bao-cao-" + dimension + ".csv");
        } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

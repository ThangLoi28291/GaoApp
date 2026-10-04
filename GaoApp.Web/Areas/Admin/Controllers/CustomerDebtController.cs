using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/customer-debt")]
[Authorize(Policy = PermissionCodes.CustomerDebt.View)]
[AutoValidateAntiforgeryToken]
public sealed partial class CustomerDebtController(ICustomerReceivableService receivables) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? customerId, string? search, CancellationToken ct, int page = 1, string? status = null)
    {
        ViewBag.Search = search;
        ViewBag.Status = status;
        return View(await receivables.GetAsync(customerId, search, ct, page, status));
    }

    [HttpPost("collect")]
    [Authorize(Policy = PermissionCodes.CustomerDebt.Collect)]
    public async Task<IActionResult> Collect([FromBody] CollectCustomerDebtRequest request, CancellationToken ct)
        => Ok(new { receiptId = await receivables.CollectAsync(request, ct) });

    [HttpGet("export")]
    public async Task<IActionResult> Export(int customerId, CancellationToken ct)
    {
        var data = await receivables.GetAsync(customerId, null, ct);
        using var book = new ClosedXML.Excel.XLWorkbook();
        var sheet = book.AddWorksheet("Cong no");
        sheet.Cell(1, 1).Value = "ĐỐI CHIẾU CÔNG NỢ";
        sheet.Cell(2, 1).Value = data.CustomerName;
        sheet.Cell(3, 1).Value = "Còn phải thu";
        sheet.Cell(3, 2).Value = data.Orders.Sum(x => x.Balance);
        var headings = new[] { "Đơn hàng", "Ngày hẹn trả", "Tổng đơn", "Đã thu", "Còn nợ" };
        for (var i = 0; i < headings.Length; i++) sheet.Cell(5, i + 1).Value = headings[i];
        var row = 6;
        foreach (var order in data.Orders)
        {
            sheet.Cell(row, 1).Value = order.Number;
            sheet.Cell(row, 2).Value = order.DueDate?.ToString("dd/MM/yyyy");
            sheet.Cell(row, 3).Value = order.Total;
            sheet.Cell(row, 4).Value = order.Paid;
            sheet.Cell(row++, 5).Value = order.Balance;
        }
        sheet.Columns().AdjustToContents();
        sheet.Columns(3, 5).Style.NumberFormat.Format = "#,##0";
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"cong-no-{customerId}.xlsx");
    }
}

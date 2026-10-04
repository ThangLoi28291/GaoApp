using ClosedXML.Excel;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed partial class CustomerDebtController
{
    [HttpGet("collections")]
    public async Task<IActionResult> Collections([FromQuery] DebtCollectionQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc báo cáo không hợp lệ." });
        return View(await receivables.GetCollectionReportAsync(query, ct));
    }

    [HttpGet("receipts/{receiptId:int}")]
    public async Task<IActionResult> Receipt(int receiptId, CancellationToken ct)
        => View(await receivables.GetReceiptAsync(receiptId, ct));

    [HttpGet("collections/export")]
    public async Task<IActionResult> ExportCollections([FromQuery] DebtCollectionQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc báo cáo không hợp lệ." });
        var data = await receivables.GetCollectionReportAsync(query, ct, export: true);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Thu cong no");
        sheet.Cell(1, 1).Value = "BÁO CÁO THU CÔNG NỢ";
        sheet.Cell(2, 1).Value = $"Từ {query.From:dd/MM/yyyy} đến {query.To:dd/MM/yyyy} · UTC+7";
        sheet.Cell(3, 1).Value = $"Khách: {data.CustomerName ?? "Tất cả"}; tìm kiếm: {query.Search ?? "—"}; phương thức: {(query.Method == null ? "Tất cả" : query.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản")}; người thu: {data.Collectors.FirstOrDefault(x => x.Id == query.UserId)?.Name ?? (query.UserId?.ToString() ?? "Tất cả")}; ca: {query.ShiftId?.ToString() ?? "Tất cả"}";
        sheet.Cell(4, 1).Value = "Tổng thu"; sheet.Cell(4, 2).Value = data.Total;
        sheet.Cell(4, 3).Value = "Tiền mặt"; sheet.Cell(4, 4).Value = data.CashTotal;
        sheet.Cell(4, 5).Value = "Chuyển khoản"; sheet.Cell(4, 6).Value = data.BankTotal;
        sheet.Cell(5, 1).Value = $"{data.TotalReceipts} phiếu · {data.CustomerCount} khách · Toàn bộ kết quả theo bộ lọc";
        var headers = new[] { "Phiếu thu", "Thời gian (UTC+7)", "Khách hàng", "Điện thoại", "Số tiền", "Phương thức", "Tài khoản nhận", "Người thu", "Mã ca", "Tham chiếu", "Ghi chú" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(7, i + 1).Value = headers[i];
        var row = 8;
        foreach (var r in data.Rows)
        {
            sheet.Cell(row, 1).Value = $"PTCN-{r.Id:D6}";
            sheet.Cell(row, 2).Value = r.At.AddHours(7);
            sheet.Cell(row, 2).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            sheet.Cell(row, 3).Value = r.CustomerName;
            sheet.Cell(row, 4).Value = r.Phone;
            sheet.Cell(row, 5).Value = r.Amount;
            sheet.Cell(row, 6).Value = r.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
            sheet.Cell(row, 7).Value = r.Bank;
            sheet.Cell(row, 8).Value = r.UserName ?? $"NV #{r.UserId}";
            sheet.Cell(row, 9).Value = r.ShiftId;
            sheet.Cell(row, 10).Value = r.Reference;
            sheet.Cell(row++, 11).Value = r.Note;
        }
        sheet.Range(7, 1, 7, headers.Length).Style.Font.Bold = true;
        sheet.Range(7, 1, Math.Max(7, row - 1), headers.Length).SetAutoFilter();
        sheet.Column(5).Style.NumberFormat.Format = "#,##0";
        foreach (var column in new[] { 2, 4, 6 }) sheet.Cell(4, column).Style.NumberFormat.Format = "#,##0";
        sheet.Columns().AdjustToContents(7, Math.Max(8, row - 1));
        foreach (var column in sheet.ColumnsUsed()) column.Width = Math.Clamp(column.Width, 12, 45);
        sheet.SheetView.FreezeRows(7);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"thu-cong-no-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.xlsx");
    }
}

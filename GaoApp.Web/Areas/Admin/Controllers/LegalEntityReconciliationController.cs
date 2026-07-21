using ClosedXML.Excel;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/legal-entities/reconciliation")]
[Authorize(Policy = PermissionCodes.System.LegalEntity.Reconcile)]
public sealed class LegalEntityReconciliationController : BaseAdminController
{
    private readonly ILegalEntityReconciliationService _service;
    private readonly IAuthorizationService _authorization;

    public LegalEntityReconciliationController(
        ILegalEntityReconciliationService service,
        IAuthorizationService authorization)
    {
        _service = service;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] LegalEntityReconciliationQueryDto query,
        CancellationToken ct)
    {
        var model = await _service.GetReportAsync(query, ct);
        var canExport = await _authorization.AuthorizeAsync(
            User,
            PermissionCodes.Report.Sales.Export);
        ViewBag.CanExport = canExport.Succeeded;
        ViewData["Title"] = "Đối soát HKD";
        return View(model);
    }

    [HttpGet("export")]
    [Authorize(Policy = PermissionCodes.Report.Sales.Export)]
    public async Task<IActionResult> Export(
        [FromQuery] LegalEntityReconciliationQueryDto query,
        CancellationToken ct)
    {
        var report = await _service.GetExportAsync(query, ct);

        using var workbook = new XLWorkbook();
        WriteSummarySheet(workbook, report);
        WriteOrderSheet(workbook, report);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"doi-soat-hkd-{report.Query.FromDate:yyyyMMdd}-{report.Query.ToDate:yyyyMMdd}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void WriteSummarySheet(
        XLWorkbook workbook,
        LegalEntityReconciliationReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Theo HKD");
        sheet.Cell(1, 1).Value = "ĐỐI SOÁT PHÂN BỔ LEGAL ENTITY";
        sheet.Range(1, 1, 1, 11).Merge().Style.Font.SetBold().Font.SetFontSize(16);
        sheet.Cell(2, 1).Value = $"Kỳ: {report.Query.FromDate:dd/MM/yyyy} - {report.Query.ToDate:dd/MM/yyyy}";
        sheet.Range(2, 1, 2, 11).Merge();

        var headers = new[]
        {
            "HKD", "Tên", "Ưu tiên", "Số đơn", "Phân bổ gốc", "Đã đảo/trả",
            "Phân bổ ròng", "HĐ kỳ vọng", "HĐ thực tế", "Chênh lệch HĐ", "Đã phát hành"
        };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(4, i + 1).Value = headers[i];

        var row = 5;
        foreach (var item in report.EntitySummaries)
        {
            sheet.Cell(row, 1).Value = item.LegalEntityCode;
            sheet.Cell(row, 2).Value = item.LegalEntityName;
            sheet.Cell(row, 3).Value = item.SalePriority;
            sheet.Cell(row, 4).Value = item.OrderCount;
            sheet.Cell(row, 5).Value = item.AllocationGrossTotal;
            sheet.Cell(row, 6).Value = item.ReversalTotal;
            sheet.Cell(row, 7).Value = item.AllocationNetTotal;
            sheet.Cell(row, 8).Value = item.ExpectedInvoiceTotal;
            sheet.Cell(row, 9).Value = item.ActualInvoiceTotal;
            sheet.Cell(row, 10).Value = item.InvoiceDifference;
            sheet.Cell(row, 11).Value = $"{item.IssuedInvoiceCount}/{item.InvoiceCount}";
            row++;
        }

        FormatTable(sheet, 4, Math.Max(4, row - 1), headers.Length);
        sheet.Columns(5, 10).Style.NumberFormat.Format = "#,##0";
        sheet.Columns().AdjustToContents();
    }

    private static void WriteOrderSheet(
        XLWorkbook workbook,
        LegalEntityReconciliationReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Theo đơn");
        var headers = new[]
        {
            "Order ID", "Mã đơn", "Ngày chốt", "Trạng thái", "HKD", "Tổng đơn gộp",
            "Phân bổ gốc", "Đã đảo/trả", "Phân bổ ròng", "HĐ kỳ vọng", "HĐ thực tế",
            "Lệch đơn", "Lệch HĐ", "Kết quả"
        };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var item in report.Orders.Items)
        {
            sheet.Cell(row, 1).Value = item.OrderId;
            sheet.Cell(row, 2).Value = item.OrderNumber ?? string.Empty;
            sheet.Cell(row, 3).Value = item.CompletedAtUtc?.ToLocalTime();
            sheet.Cell(row, 4).Value = item.Status;
            sheet.Cell(row, 5).Value = item.LegalEntityCodes;
            sheet.Cell(row, 6).Value = item.UnifiedOrderTotal;
            sheet.Cell(row, 7).Value = item.AllocationGrossTotal;
            sheet.Cell(row, 8).Value = item.ReversalTotal;
            sheet.Cell(row, 9).Value = item.AllocationNetTotal;
            sheet.Cell(row, 10).Value = item.ExpectedInvoiceTotal;
            sheet.Cell(row, 11).Value = item.ActualInvoiceTotal;
            sheet.Cell(row, 12).Value = item.OrderDifference;
            sheet.Cell(row, 13).Value = item.InvoiceDifference;
            sheet.Cell(row, 14).Value = item.IsBalanced ? "Khớp" : "Cần kiểm tra";
            row++;
        }

        FormatTable(sheet, 1, Math.Max(1, row - 1), headers.Length);
        sheet.Column(3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        sheet.Columns(6, 13).Style.NumberFormat.Format = "#,##0";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void FormatTable(IXLWorksheet sheet, int headerRow, int lastRow, int lastColumn)
    {
        var header = sheet.Range(headerRow, 1, headerRow, lastColumn);
        header.Style.Font.SetBold();
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#3556F5"));
        header.Style.Font.SetFontColor(XLColor.White);
        if (lastRow > headerRow)
            sheet.Range(headerRow, 1, lastRow, lastColumn).SetAutoFilter();
    }
}

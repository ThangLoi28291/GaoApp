using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POSShiftDashboards;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Route("admin/pos/shift")]
[Authorize] // bắt buộc đăng nhập trước
[AutoValidateAntiforgeryToken]
public class POSShiftController : ControllerBase
{
    private readonly IPOSShiftService _service;

    public POSShiftController(IPOSShiftService service)
    {
        _service = service;
    }

    /// <summary>
    /// GET: /admin/pos/shift/current
    /// Lấy ca POS đang mở hiện tại
    /// </summary>
    [HttpGet("current")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var shift = await _service.GetCurrentOpenAsync(ct);
        return Ok(shift);
    }

    /// <summary>
    /// POST: /admin/pos/shift/open
    /// Mở ca mới
    /// </summary>
    [HttpPost("open")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Open)]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest req, CancellationToken ct)
    {
        var result = await _service.OpenAsync(req, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST: /admin/pos/shift/close
    /// Đóng ca hiện tại
    /// </summary>
    [HttpPost("close")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Close)]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequest req, CancellationToken ct)
    {
        var result = await _service.CloseAsync(req, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tạo phiếu thu/chi trong ca hiện tại
    /// POST: /admin/pos/shift/cash-transaction
    /// </summary>
    [HttpPost("cash-transaction")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Reconcile)]
    public async Task<IActionResult> AddCashTransaction([FromBody] CreatePosShiftCashTransactionRequest dto, CancellationToken ct)
    {
        var result = await _service.AddCashTransactionAsync(dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách phiếu thu/chi của ca đang mở
    /// GET: /admin/pos/shift/cash-transactions
    /// </summary>
    [HttpGet("cash-transactions")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetCashTransactions(CancellationToken ct)
    {
        var result = await _service.GetCashTransactionsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Tóm tắt số liệu ca hiện tại / ca theo id
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetSummary([FromQuery] int? shiftId, CancellationToken ct)
    {
        var result = await _service.GetSummaryAsync(shiftId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Alias lấy ca mở hiện tại
    /// </summary>
    [HttpGet("active")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await _service.GetCurrentOpenAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Lịch sử ca POS
    /// </summary>
    [HttpGet("history")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] POSShiftStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtcExclusive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var req = new QueryPOSShiftHistoryRequest
        {
            Status = status,
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            Page = page,
            PageSize = pageSize
        };

        var result = await _service.QueryHistoryAsync(req, ct);
        return Ok(result);
    }
    /// <summary>
    /// GET: /admin/pos/shift/ownership-info
    /// Lấy thông tin ca đang mở trên terminal hiện tại.
    /// Dùng cho popup khi ca thuộc nhân viên khác.
    /// </summary>
    [HttpGet("ownership-info")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetOwnershipInfo(CancellationToken ct)
    {
        var result = await _service.GetCurrentOwnershipInfoAsync(ct);

        return Ok(result);
    }

    /// <summary>
    /// POST: /admin/pos/shift/takeover
    /// Quản lý/Admin tiếp quản ca đang mở của nhân viên khác.
    /// </summary>
    [HttpPost("takeover")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.TakeOver)]
    public async Task<IActionResult> TakeOver(
        [FromBody] TakeOverPOSShiftRequest request,
        CancellationToken ct)
    {
        var result = await _service.TakeOverAsync(request, ct);

        return Ok(new
        {
            success = true,
            message = "Đã tiếp quản ca POS thành công.",
            data = result
        });
    }

    /// <summary>
    /// POST: /admin/pos/shift/force-close
    /// Quản lý/Admin đóng hộ ca đang mở của nhân viên khác.
    /// </summary>
    [HttpPost("force-close")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.ForceClose)]
    public async Task<IActionResult> ForceClose(
        [FromBody] ForceClosePOSShiftRequest request,
        CancellationToken ct)
    {
        var result = await _service.ForceCloseAsync(request, ct);

        return Ok(new
        {
            success = true,
            message = "Đã đóng hộ ca POS thành công.",
            data = result
        });
    }
    /// <summary>
    /// GET: /admin/pos/shift/{shiftId}/print?entryType=Opening
    /// Lấy dữ liệu in phiếu nhận ca / đóng ca.
    /// </summary>
    [HttpGet("{shiftId:int}/print")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetPrint(
        int shiftId,
        [FromQuery] POSShiftCashDenominationEntryType entryType,
        CancellationToken ct)
    {
        var result = await _service.GetPrintAsync(shiftId, entryType, ct);
        return Ok(result);
    }
    /// <summary>
    /// GET: /admin/pos/shift/dashboard
    /// Dashboard nhanh cho ca POS đang mở hiện tại.
    /// </summary>
    [HttpGet("dashboard-data")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> DashboardData(CancellationToken ct)
    {
        var summary = await _service.GetSummaryAsync(null, ct);

        return Ok(new
        {
            shiftId = summary.Id,
            shiftCode = summary.ShiftCode,
            openedAt = summary.OpenedAtUtc,

            totalSales = summary.CompletedSalesTotal,
            cashSales = summary.CashSalesTotal,
            bankSales = summary.NonCashSalesTotal,

            ordersCount = summary.CompletedOrders,
            refundTotal = summary.RefundTotal,
            refundCount = summary.RefundCount,
            voidCount = summary.VoidCount,

            cashInTotal = summary.CashInTotal,
            cashOutTotal = summary.CashOutTotal,
            openingCash = summary.OpeningCash,
            closingCashExpected = summary.ClosingCashExpected
        });
    }

    /// <summary>
    /// Dashboard quản lý ca POS.
    /// Chỉ dành cho quản lý.
    /// </summary>
    [GaoApp.Web.Common.POS.StoreAdminOnly]
    [HttpGet("manager-dashboard")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetManagerDashboard(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtcExclusive,
        [FromQuery] int? userId,
        [FromQuery] int? terminalId,
        [FromQuery] POSShiftStatus? status,
        CancellationToken ct)
    {
        var query = new POSShiftManagerDashboardQueryDto
        {
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            UserId = userId,
            TerminalId = terminalId,
            Status = status
        };

        var result = await _service.GetManagerDashboardAsync(
            query,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Xuất Excel báo cáo quản lý ca POS.
    /// GET: /admin/pos/shift/manager-dashboard/export-excel
    /// </summary>
    [GaoApp.Web.Common.POS.StoreAdminOnly]
    [HttpGet("manager-dashboard/export-excel")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> ExportManagerDashboardExcel(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtcExclusive,
        [FromQuery] int? userId,
        [FromQuery] int? terminalId,
        [FromQuery] POSShiftStatus? status,
        CancellationToken ct)
    {
        var query = new POSShiftManagerDashboardQueryDto
        {
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            UserId = userId,
            TerminalId = terminalId,
            Status = status
        };

        var data = await _service.GetManagerDashboardAsync(query, ct);

        using var workbook = new XLWorkbook();

        CreateOverviewSheet(workbook, data, fromUtc, toUtcExclusive);
        CreateEmployeeSheet(workbook, data);
        CreateShiftSheet(workbook, data);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"BaoCaoCaPOS-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
    [HttpPost("{shiftId:int}/cash-receipt")]
    [GaoApp.Web.Common.POS.StoreAdminOnly]
    public async Task<IActionResult> ConfirmCashReceipt(
        int shiftId, [FromBody] ConfirmShiftCashReceiptRequest request,
        [FromServices] GaoApp.Application.Services.POSShifts.POSShiftCashReceiptService receipts,
        CancellationToken ct)
    {
        await receipts.ConfirmAsync(shiftId, request, ct);
        return Ok(new { success = true, message = "Đã xác nhận nhận tiền và duyệt chốt ca." });
    }

    private static void CreateOverviewSheet(
    XLWorkbook workbook,
    POSShiftManagerDashboardDto data,
    DateTime? fromUtc,
    DateTime? toUtcExclusive)
    {
        var ws = workbook.Worksheets.Add("Tổng quan");

        ws.Cell("A1").Value = "BÁO CÁO QUẢN LÝ CA POS";
        ws.Range("A1:F1").Merge();

        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 18;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell("A3").Value = "Từ ngày";
        ws.Cell("B3").Value = fromUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Tất cả";

        ws.Cell("A4").Value = "Đến trước";
        ws.Cell("B4").Value = toUtcExclusive?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Tất cả";

        var o = data.Overview;

        var row = 6;

        WriteOverviewRow(ws, row++, "Tổng số ca", o.TotalShifts);
        WriteOverviewRow(ws, row++, "Ca đang mở", o.OpenShifts);
        WriteOverviewRow(ws, row++, "Ca đã đóng", o.ClosedShifts);

        row++;

        WriteOverviewMoneyRow(ws, row++, "Tổng doanh thu", o.TotalSales);
        WriteOverviewMoneyRow(ws, row++, "Doanh thu tiền mặt", o.CashSalesTotal);
        WriteOverviewMoneyRow(ws, row++, "Doanh thu không tiền mặt", o.NonCashSalesTotal);

        row++;

        WriteOverviewMoneyRow(ws, row++, "Thu thêm vào két", o.CashInTotal);
        WriteOverviewMoneyRow(ws, row++, "Chi/rút khỏi két", o.CashOutTotal);
        WriteOverviewMoneyRow(ws, row++, "Tổng refund", o.RefundTotal);
        WriteOverviewRow(ws, row++, "Số lần refund", o.RefundCount);
        WriteOverviewRow(ws, row++, "Số lần void", o.VoidCount);

        row++;

        WriteOverviewMoneyRow(ws, row++, "Tổng tiền dự kiến", o.ClosingCashExpectedTotal);
        WriteOverviewMoneyRow(ws, row++, "Tổng tiền thực đếm", o.ClosingCashActualTotal);
        WriteOverviewMoneyRow(ws, row++, "Tổng lệch quỹ", o.CashDifferenceTotal);
        WriteOverviewRow(ws, row++, "Số ca lệch quỹ", o.DifferenceShiftCount);
        WriteOverviewRow(ws, row++, "Số ca thiếu quỹ", o.ShortageShiftCount);
        WriteOverviewRow(ws, row++, "Số ca thừa quỹ", o.OverShiftCount);

        var used = ws.RangeUsed();
        if (used != null)
        {
            used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteOverviewRow(IXLWorksheet ws, int row, string label, int value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value;

        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void WriteOverviewMoneyRow(IXLWorksheet ws, int row, string label, decimal value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value;

        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        if (value < 0)
        {
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.Red;
        }
        else if (value > 0)
        {
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.DarkGreen;
        }
    }
    private static void CreateEmployeeSheet(
    XLWorkbook workbook,
    POSShiftManagerDashboardDto data)
    {
        var ws = workbook.Worksheets.Add("Theo nhân viên");

        var headers = new[]
        {
        "Nhân viên",
        "Tổng ca",
        "Ca đang mở",
        "Ca đã đóng",
        "Tổng doanh thu",
        "Tiền mặt",
        "Không tiền mặt",
        "Refund",
        "Số refund",
        "Void",
        "Lệch quỹ",
        "Số ca lệch"
    };

        WriteHeader(ws, headers);

        var row = 2;

        foreach (var x in data.EmployeeStats)
        {
            ws.Cell(row, 1).Value = x.FullName ?? x.UserName ?? $"User #{x.UserId}";
            ws.Cell(row, 2).Value = x.TotalShifts;
            ws.Cell(row, 3).Value = x.OpenShifts;
            ws.Cell(row, 4).Value = x.ClosedShifts;
            ws.Cell(row, 5).Value = x.TotalSales;
            ws.Cell(row, 6).Value = x.CashSalesTotal;
            ws.Cell(row, 7).Value = x.NonCashSalesTotal;
            ws.Cell(row, 8).Value = x.RefundTotal;
            ws.Cell(row, 9).Value = x.RefundCount;
            ws.Cell(row, 10).Value = x.VoidCount;
            ws.Cell(row, 11).Value = x.CashDifferenceTotal;
            ws.Cell(row, 12).Value = x.DifferenceShiftCount;

            row++;
        }

        FormatTable(ws, row - 1, moneyColumns: new[] { 5, 6, 7, 8, 11 });
    }
    private static void CreateShiftSheet(
    XLWorkbook workbook,
    POSShiftManagerDashboardDto data)
    {
        var ws = workbook.Worksheets.Add("Chi tiết ca");

        var headers = new[]
        {
        "Mã ca",
        "Trạng thái",
        "Nhân viên mở",
        "Nhân viên đóng",
        "Terminal",
        "Kho",
        "Mở lúc",
        "Đóng lúc",
        "Tiền đầu ca",
        "Doanh thu",
        "Tiền mặt",
        "Không tiền mặt",
        "Thu thêm",
        "Chi ra",
        "Refund",
        "Số refund",
        "Void",
        "Tiền dự kiến",
        "Tiền thực đếm",
        "Lệch quỹ",
        "Ghi chú mở ca",
        "Ghi chú đóng ca",
        "Bàn giao tiền",
        "Admin thực nhận",
        "Lệch so với nhân viên",
        "Admin xác nhận",
        "Xác nhận lúc",
        "Ghi chú bàn giao"
    };

        WriteHeader(ws, headers);

        var row = 2;

        foreach (var x in data.Shifts)
        {
            ws.Cell(row, 1).Value = x.ShiftCode ?? $"Ca #{x.Id}";
            ws.Cell(row, 2).Value = ToShiftStatusText(x.Status);
            ws.Cell(row, 3).Value = x.OpenedByUserName ?? $"User #{x.OpenedByUserId}";
            ws.Cell(row, 4).Value = x.ClosedByUserName ?? (x.ClosedByUserId.HasValue ? $"User #{x.ClosedByUserId}" : "");
            ws.Cell(row, 5).Value = $"{x.TerminalCode} {x.TerminalName}".Trim();
            ws.Cell(row, 6).Value = $"{x.WarehouseCode} {x.WarehouseName}".Trim();

            ws.Cell(row, 7).Value = x.OpenedAtUtc.ToLocalTime();
            ws.Cell(row, 8).Value = x.ClosedAtUtc?.ToLocalTime();

            ws.Cell(row, 9).Value = x.OpeningCash;
            ws.Cell(row, 10).Value = x.TotalSales;
            ws.Cell(row, 11).Value = x.CashSalesTotal;
            ws.Cell(row, 12).Value = x.NonCashSalesTotal;
            ws.Cell(row, 13).Value = x.CashInTotal;
            ws.Cell(row, 14).Value = x.CashOutTotal;
            ws.Cell(row, 15).Value = x.RefundTotal;
            ws.Cell(row, 16).Value = x.RefundCount;
            ws.Cell(row, 17).Value = x.VoidCount;
            ws.Cell(row, 18).Value = x.ClosingCashExpected;
            ws.Cell(row, 19).Value = x.ClosingCashActual;
            ws.Cell(row, 20).Value = x.CashDifference;
            ws.Cell(row, 21).Value = x.OpenNote;
            ws.Cell(row, 22).Value = x.CloseNote;
            ws.Cell(row, 23).Value = x.CashReceivedAtUtc.HasValue ? "Đã nhận tiền / Đã duyệt" : x.Status == POSShiftStatus.Closed ? "Chờ nhận tiền" : "Chưa chốt ca";
            ws.Cell(row, 24).Value = x.CashReceivedAmount;
            ws.Cell(row, 25).Value = x.CashReceivedAmount - x.ClosingCashActual;
            ws.Cell(row, 26).Value = x.CashReceivedByUserName;
            ws.Cell(row, 27).Value = x.CashReceivedAtUtc?.ToLocalTime();
            ws.Cell(row, 28).Value = x.CashReceiptNote;

            row++;
        }

        FormatTable(
            ws,
            row - 1,
            moneyColumns: new[] { 9, 10, 11, 12, 13, 14, 15, 18, 19, 20, 24, 25 },
            dateColumns: new[] { 7, 8, 27 });

        ws.SheetView.FreezeRows(1);
    }
    private static void WriteHeader(IXLWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f4e78");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ws.Row(1).Height = 24;
    }

    private static void FormatTable(
        IXLWorksheet ws,
        int lastRow,
        int[]? moneyColumns = null,
        int[]? dateColumns = null)
    {
        if (lastRow < 1)
            lastRow = 1;

        var used = ws.Range(1, 1, lastRow, ws.LastColumnUsed()?.ColumnNumber() ?? 1);

        used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        used.SetAutoFilter();

        ws.SheetView.FreezeRows(1);

        if (moneyColumns != null)
        {
            foreach (var col in moneyColumns)
            {
                ws.Column(col).Style.NumberFormat.Format = "#,##0";
                ws.Column(col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        if (dateColumns != null)
        {
            foreach (var col in dateColumns)
            {
                ws.Column(col).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            }
        }

        ws.Columns().AdjustToContents();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var diffCell = row.Cell(20);

            if (decimal.TryParse(diffCell.Value.ToString(), out var diff))
            {
                if (diff < 0)
                {
                    diffCell.Style.Font.FontColor = XLColor.Red;
                    diffCell.Style.Font.Bold = true;
                }
                else if (diff > 0)
                {
                    diffCell.Style.Font.FontColor = XLColor.DarkGreen;
                    diffCell.Style.Font.Bold = true;
                }
            }
        }

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
    }

    private static string ToShiftStatusText(POSShiftStatus status)
    {
        return status switch
        {
            POSShiftStatus.Open => "Đang mở",
            POSShiftStatus.Closed => "Đã đóng",
            _ => status.ToString()
        };
    }
}

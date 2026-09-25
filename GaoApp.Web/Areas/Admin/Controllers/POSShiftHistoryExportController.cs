using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

/// <summary>
/// Xuất báo cáo một ca POS từ màn Lịch sử ca.
///
/// Tách route riêng để:
/// - không thay đổi semantic các endpoint POSShiftController hiện hữu;
/// - bắt buộc quyền report.shift.export ở server;
/// - tái sử dụng GetSummaryAsync, bao gồm ownership check của màn lịch sử;
/// - không tạo business calculation mới ở Web.
/// </summary>
[Area("Admin")]
[ApiController]
[Route("admin/pos/shift/history-export")]
[Authorize(Policy = PermissionCodes.Report.Shift.Export)]
public sealed class POSShiftHistoryExportController : ControllerBase
{
    private readonly IPOSShiftService _shiftService;

    public POSShiftHistoryExportController(
        IPOSShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    /// <summary>
    /// GET /admin/pos/shift/history-export/{shiftId}
    ///
    /// Xuất CSV UTF-8 có BOM.
    /// Dữ liệu lấy từ canonical POSShiftSummaryDto của service.
    /// </summary>
    [HttpGet("{shiftId:int}")]
    public async Task<IActionResult> ExportCsv(
        int shiftId,
        CancellationToken ct = default)
    {
        if (shiftId <= 0)
        {
            return BadRequest(new
            {
                message = "Ca POS cần xuất không hợp lệ."
            });
        }

        var summary =
            await _shiftService.GetSummaryAsync(
                shiftId,
                ct);

        var csv =
            BuildCsv(summary);

        // Prefix U+FEFF để Excel nhận đúng UTF-8 tiếng Việt.
        var bytes =
            Encoding.UTF8.GetBytes(
                "\uFEFF" + csv);

        var safeShiftCode =
            SafeFilePart(
                summary.ShiftCode,
                $"SHIFT-{summary.Id}");

        var fileName =
            $"bao-cao-ca-{safeShiftCode}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }

    private static string BuildCsv(
        POSShiftSummaryDto summary)
    {
        var sb =
            new StringBuilder();

        // Excel nhận delimiter đúng ngay cả trên máy dùng locale vi-VN.
        sb.AppendLine("sep=;");

        AppendRow(
            sb,
            "BÁO CÁO CA POS");

        AppendRow(
            sb,
            "Mã ca",
            summary.ShiftCode ??
            $"SHIFT-{summary.Id}");

        AppendRow(
            sb,
            "Shift ID",
            IntValue(summary.Id));

        AppendRow(
            sb,
            "Trạng thái",
            summary.Status.ToString());

        AppendRow(
            sb,
            "Mở lúc (UTC)",
            UtcValue(summary.OpenedAtUtc));

        AppendRow(
            sb,
            "Đóng lúc (UTC)",
            UtcValue(summary.ClosedAtUtc));

        AppendRow(sb);

        AppendRow(
            sb,
            "TỔNG QUAN ĐƠN HÀNG");

        AppendRow(
            sb,
            "Tổng đơn",
            IntValue(summary.TotalOrders));

        AppendRow(
            sb,
            "Đơn hoàn tất",
            IntValue(summary.CompletedOrders));

        AppendRow(
            sb,
            "Đơn nháp",
            IntValue(summary.DraftOrders));

        AppendRow(
            sb,
            "Đơn đã hủy",
            IntValue(summary.CancelledOrders));

        AppendRow(
            sb,
            "Số lượt hoàn tiền",
            IntValue(summary.RefundCount));

        AppendRow(
            sb,
            "Số lượt void",
            IntValue(summary.VoidCount));

        AppendRow(
            sb,
            "Tổng bán hoàn tất",
            DecimalValue(
                summary.CompletedSalesTotal));

        AppendRow(sb);

        AppendRow(
            sb,
            "TÀI CHÍNH CA");

        AppendRow(
            sb,
            "Tiền đầu ca",
            DecimalValue(
                summary.OpeningCash));

        AppendRow(
            sb,
            "Doanh thu tiền mặt",
            DecimalValue(
                summary.CashSalesTotal));

        AppendRow(
            sb,
            "Doanh thu không tiền mặt",
            DecimalValue(
                summary.NonCashSalesTotal));

        AppendRow(
            sb,
            "Thu thêm",
            DecimalValue(
                summary.CashInTotal));

        AppendRow(
            sb,
            "Chi ra",
            DecimalValue(
                summary.CashOutTotal));

        AppendRow(
            sb,
            "Hoàn tiền mặt",
            DecimalValue(
                summary.CashRefundTotal));

        AppendRow(
            sb,
            "Hoàn không tiền mặt",
            DecimalValue(
                summary.NonCashRefundTotal));

        AppendRow(
            sb,
            "Tổng hoàn tiền",
            DecimalValue(
                summary.RefundTotal));

        AppendRow(
            sb,
            "Tiền cuối ca dự kiến",
            DecimalValue(
                summary.ClosingCashExpected));

        AppendRow(
            sb,
            "Tiền cuối ca thực tế",
            NullableDecimalValue(
                summary.ClosingCashActual));

        AppendRow(sb);

        AppendRow(
            sb,
            "GHI CHÚ CA");

        AppendRow(
            sb,
            "Ghi chú mở ca",
            summary.OpenNote);

        AppendRow(
            sb,
            "Ghi chú đóng ca",
            summary.CloseNote);

        AppendRow(sb);

        AppendRow(
            sb,
            "DANH SÁCH ĐƠN");

        AppendRow(
            sb,
            "ID",
            "Mã đơn",
            "Trạng thái",
            "Thanh toán",
            "Tạo lúc (UTC)",
            "Hoàn tất lúc (UTC)",
            "Tổng tiền");

        foreach (
            var order in
            summary.Orders ??
            new List<POSShiftSummaryOrderDto>())
        {
            AppendRow(
                sb,
                IntValue(order.Id),
                order.OrderNumber,
                order.Status.ToString(),
                order.PaymentStatus.ToString(),
                UtcValue(order.CreatedAtUtc),
                UtcValue(order.CompletedAtUtc),
                DecimalValue(order.GrandTotal));
        }

        AppendRow(sb);

        AppendRow(
            sb,
            "THU / CHI TIỀN MẶT");

        AppendRow(
            sb,
            "ID",
            "Thời gian (UTC)",
            "Loại",
            "Số tiền",
            "Lý do",
            "Ghi chú");

        foreach (
            var transaction in
            summary.CashTransactions ??
            new List<POSShiftCashTransactionDto>())
        {
            AppendRow(
                sb,
                IntValue(transaction.Id),
                UtcValue(
                    transaction.CreatedAtUtc),
                transaction.Type,
                DecimalValue(
                    transaction.Amount),
                transaction.Reason,
                transaction.Note);
        }

        return sb.ToString();
    }

    private static void AppendRow(
        StringBuilder sb,
        params string?[] values)
    {
        if (
            values is null ||
            values.Length == 0)
        {
            sb.AppendLine();
            return;
        }

        sb.AppendLine(
            string.Join(
                ';',
                values.Select(
                    EscapeCell)));
    }

    /// <summary>
    /// Escape CSV và chặn spreadsheet formula injection
    /// ở mọi field text có thể do người dùng nhập.
    /// </summary>
    private static string EscapeCell(
        string? value)
    {
        var text =
            (value ?? string.Empty)
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var trimmed =
            text.TrimStart();

        if (
            trimmed.Length > 0 &&
            trimmed[0] is
                '=' or
                '+' or
                '-' or
                '@')
        {
            text =
                "'" + text;
        }

        return
            "\"" +
            text.Replace(
                "\"",
                "\"\"",
                StringComparison.Ordinal) +
            "\"";
    }

    private static string IntValue(
        int value)
        => value.ToString(
            CultureInfo.InvariantCulture);

    private static string DecimalValue(
        decimal value)
        => value.ToString(
            "0.##",
            CultureInfo.InvariantCulture);

    private static string NullableDecimalValue(
        decimal? value)
        => value.HasValue
            ? DecimalValue(
                value.Value)
            : string.Empty;

    /// <summary>
    /// Field tên Utc được xuất đúng clock value đang lưu,
    /// không tự đổi timezone tại tầng export.
    /// </summary>
    private static string UtcValue(
        DateTime value)
        => value.ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);

    private static string UtcValue(
        DateTime? value)
        => value.HasValue
            ? UtcValue(
                value.Value)
            : string.Empty;

    private static string SafeFilePart(
        string? value,
        string fallback)
    {
        var text =
            string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();

        foreach (
            var invalid in
            Path.GetInvalidFileNameChars())
        {
            text =
                text.Replace(
                    invalid,
                    '-');
        }

        text =
            text.Replace(
                ' ',
                '-');

        while (
            text.Contains(
                "--",
                StringComparison.Ordinal))
        {
            text =
                text.Replace(
                    "--",
                    "-",
                    StringComparison.Ordinal);
        }

        text =
            text.Trim(
                '-',
                '.');

        return string.IsNullOrWhiteSpace(text)
            ? fallback
            : text;
    }
}

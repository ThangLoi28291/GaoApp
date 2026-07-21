using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

/// <summary>
/// Bộ lọc tra cứu lịch sử ca POS.
/// </summary>
public class QueryPOSShiftHistoryRequest
{
    /// <summary>
    /// Lọc theo nhân viên mở ca.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Lọc theo terminal.
    /// </summary>
    public int? TerminalId { get; set; }

    /// <summary>
    /// Lọc theo trạng thái ca.
    /// </summary>
    public POSShiftStatus? Status { get; set; }

    /// <summary>
    /// Thời gian mở ca từ.
    /// </summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>
    /// Thời gian mở ca đến trước.
    /// </summary>
    public DateTime? ToUtcExclusive { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
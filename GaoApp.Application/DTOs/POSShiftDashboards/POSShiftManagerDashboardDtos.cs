using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShiftDashboards;

/// <summary>
/// Request lọc dashboard quản lý ca POS.
/// Dùng cho quản lý xem theo ngày, nhân viên, terminal.
/// </summary>
public sealed class POSShiftManagerDashboardQueryDto
{
    /// <summary>
    /// Từ thời gian mở ca.
    /// Frontend truyền UTC.
    /// </summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>
    /// Đến trước thời gian này.
    /// Frontend truyền UTC.
    /// </summary>
    public DateTime? ToUtcExclusive { get; set; }

    /// <summary>
    /// Lọc theo nhân viên mở ca.
    /// Null = tất cả nhân viên.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Lọc theo terminal.
    /// Null = tất cả terminal.
    /// </summary>
    public int? TerminalId { get; set; }

    /// <summary>
    /// Lọc theo trạng thái ca.
    /// Null = tất cả.
    /// </summary>
    public POSShiftStatus? Status { get; set; }
}

/// <summary>
/// Response chính của Dashboard quản lý ca POS.
/// </summary>
public sealed class POSShiftManagerDashboardDto
{
    public POSShiftManagerOverviewDto Overview { get; set; } = new();

    public List<POSShiftManagerEmployeeStatDto> EmployeeStats { get; set; } = new();

    public List<POSShiftManagerShiftItemDto> Shifts { get; set; } = new();

    public List<POSShiftManagerShiftItemDto> DifferenceShifts { get; set; } = new();
}

/// <summary>
/// Tổng quan số liệu toàn bộ ca theo bộ lọc.
/// </summary>
public sealed class POSShiftManagerOverviewDto
{
    public int TotalShifts { get; set; }

    public int OpenShifts { get; set; }

    public int ClosedShifts { get; set; }

    public decimal TotalSales { get; set; }

    public decimal CashSalesTotal { get; set; }

    public decimal NonCashSalesTotal { get; set; }

    public decimal CashInTotal { get; set; }

    public decimal CashOutTotal { get; set; }

    public decimal RefundTotal { get; set; }

    public int RefundCount { get; set; }

    public int VoidCount { get; set; }

    /// <summary>
    /// Tổng tiền thực đếm của các ca đã đóng.
    /// </summary>
    public decimal ClosingCashActualTotal { get; set; }

    /// <summary>
    /// Tổng tiền dự kiến của các ca đã đóng.
    /// </summary>
    public decimal ClosingCashExpectedTotal { get; set; }

    /// <summary>
    /// Tổng lệch quỹ = Actual - Expected.
    /// Dương = thừa, âm = thiếu.
    /// </summary>
    public decimal CashDifferenceTotal { get; set; }

    public int DifferenceShiftCount { get; set; }

    public int ShortageShiftCount { get; set; }

    public int OverShiftCount { get; set; }
}

/// <summary>
/// Thống kê theo từng nhân viên.
/// </summary>
public sealed class POSShiftManagerEmployeeStatDto
{
    public int UserId { get; set; }

    public string? UserName { get; set; }

    public string? FullName { get; set; }

    public int TotalShifts { get; set; }

    public int OpenShifts { get; set; }

    public int ClosedShifts { get; set; }

    public decimal TotalSales { get; set; }

    public decimal CashSalesTotal { get; set; }

    public decimal NonCashSalesTotal { get; set; }

    public decimal RefundTotal { get; set; }

    public int RefundCount { get; set; }

    public int VoidCount { get; set; }

    public decimal CashDifferenceTotal { get; set; }

    public int DifferenceShiftCount { get; set; }
}

/// <summary>
/// Một dòng ca trong dashboard quản lý.
/// </summary>
public sealed class POSShiftManagerShiftItemDto
{
    public int Id { get; set; }

    public string? ShiftCode { get; set; }

    public POSShiftStatus Status { get; set; }

    public int OpenedByUserId { get; set; }

    public string? OpenedByUserName { get; set; }

    public int? ClosedByUserId { get; set; }

    public string? ClosedByUserName { get; set; }

    public int TerminalId { get; set; }

    public string? TerminalCode { get; set; }

    public string? TerminalName { get; set; }

    public int WarehouseId { get; set; }

    public string? WarehouseCode { get; set; }

    public string? WarehouseName { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public decimal OpeningCash { get; set; }

    public decimal CashSalesTotal { get; set; }

    public decimal NonCashSalesTotal { get; set; }

    public decimal TotalSales => CashSalesTotal + NonCashSalesTotal;

    public decimal CashInTotal { get; set; }

    public decimal CashOutTotal { get; set; }

    public decimal RefundTotal { get; set; }

    public int RefundCount { get; set; }

    public int VoidCount { get; set; }

    public decimal ClosingCashExpected { get; set; }

    public decimal? ClosingCashActual { get; set; }

    public decimal CashDifference { get; set; }

    public string? OpenNote { get; set; }

    public string? CloseNote { get; set; }
}
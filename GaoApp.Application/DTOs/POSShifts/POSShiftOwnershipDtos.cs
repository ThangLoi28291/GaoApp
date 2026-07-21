namespace GaoApp.Application.DTOs.POSShifts;

/// <summary>
/// Thông tin ca đang mở nhưng thuộc nhân viên khác.
/// Dùng để frontend hiển thị popup rõ ràng.
/// </summary>
public sealed class POSShiftOwnershipInfoDto
{
    public int ShiftId { get; set; }
    public string? ShiftCode { get; set; }

    public int StoreId { get; set; }

    public int TerminalId { get; set; }
    public string? TerminalCode { get; set; }
    public string? TerminalName { get; set; }

    public int OpenedByUserId { get; set; }
    public string? OpenedByUserName { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    public int WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }

    public decimal OpeningCash { get; set; }
    public decimal ClosingCashExpected { get; set; }

    public bool IsOwnedByCurrentUser { get; set; }
    public bool CanTakeOver { get; set; }
    public bool CanForceClose { get; set; }
}

/// <summary>
/// Request tiếp quản ca.
/// </summary>
public sealed class TakeOverPOSShiftRequest
{
    public int ShiftId { get; set; }

    /// <summary>
    /// Lý do tiếp quản. Bắt buộc để sau này tra cứu.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Request đóng hộ ca.
/// </summary>
public sealed class ForceClosePOSShiftRequest
{
    public int ShiftId { get; set; }

    public decimal ClosingCashActual { get; set; }

    /// <summary>
    /// Lý do đóng hộ. Bắt buộc.
    /// </summary>
    public string? Reason { get; set; }

    public string? Note { get; set; }
}
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public class POSShiftHandoverSlipDto
{
    public int Id { get; set; }

    public string SlipCode { get; set; } = default!;

    public string BarcodeValue { get; set; } = default!;

    public POSShiftHandoverSlipStatus Status { get; set; }

    public int? TerminalId { get; set; }
    public string? TerminalCode { get; set; }
    public string? TerminalName { get; set; }

    public int WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }

    public int CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }

    public int? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }

    public decimal OpeningCashTotal { get; set; }

    public int? UsedPOSShiftId { get; set; }
    public string? UsedPOSShiftCode { get; set; }

    public DateTime? PrintedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public int? UsedByUserId { get; set; }
    public string? UsedByUserName { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<POSShiftHandoverSlipDenominationDto> Denominations { get; set; } = new();
}
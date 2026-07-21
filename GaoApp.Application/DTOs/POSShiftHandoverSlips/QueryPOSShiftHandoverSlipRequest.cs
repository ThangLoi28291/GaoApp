using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public class QueryPOSShiftHandoverSlipRequest
{
    public POSShiftHandoverSlipStatus? Status { get; set; }

    public int? TerminalId { get; set; }

    public int? WarehouseId { get; set; }

    public int? AssignedToUserId { get; set; }

    public string? Keyword { get; set; }

    public DateTime? FromUtc { get; set; }

    public DateTime? ToUtcExclusive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
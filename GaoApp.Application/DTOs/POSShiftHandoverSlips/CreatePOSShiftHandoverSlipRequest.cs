using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public class CreatePOSShiftHandoverSlipRequest
{
    public int? TerminalId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho bán hàng.")]
    public int WarehouseId { get; set; }

    public int? AssignedToUserId { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public List<CreatePOSShiftHandoverSlipDenominationRequest> Denominations { get; set; } = new();
}

public class CreatePOSShiftHandoverSlipDenominationRequest
{
    public int DenominationValue { get; set; }

    public int Quantity { get; set; }
}
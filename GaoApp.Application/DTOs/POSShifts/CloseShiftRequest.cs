namespace GaoApp.Application.DTOs.POSShifts;

public class CloseShiftRequest
{
    public decimal ClosingCashActual { get; set; }

    public string? Note { get; set; }

    // Chi tiết số tờ theo mệnh giá lúc đóng ca.
    public List<POSShiftDenominationRequest> Denominations { get; set; } = new();
}
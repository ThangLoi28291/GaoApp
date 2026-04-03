namespace GaoApp.Application.DTOs.POSShifts;

public class CloseShiftRequest
{
    public decimal ClosingCashActual { get; set; }
    public string? Note { get; set; }
}
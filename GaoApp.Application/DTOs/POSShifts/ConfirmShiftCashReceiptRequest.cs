namespace GaoApp.Application.DTOs.POSShifts;

public sealed class ConfirmShiftCashReceiptRequest
{
    public decimal? ReceivedAmount { get; set; }
    public string? Note { get; set; }
}

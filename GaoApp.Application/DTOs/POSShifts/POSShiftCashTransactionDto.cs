namespace GaoApp.Application.DTOs.POSShifts;

public class POSShiftCashTransactionDto
{
    public int Id { get; set; }

    public int POSShiftId { get; set; }

    public string Type { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
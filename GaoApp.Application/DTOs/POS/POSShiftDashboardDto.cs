namespace GaoApp.Application.DTOs.POS;

public sealed class POSShiftDashboardDto
{
    public int ShiftId { get; set; }

    public string? ShiftCode { get; set; }

    public DateTime OpenedAt { get; set; }

    public decimal CashSales { get; set; }

    public decimal BankSales { get; set; }

    public decimal TotalSales { get; set; }

    public int OrdersCount { get; set; }

    public int VoidCount { get; set; }

    public int RefundCount { get; set; }

}
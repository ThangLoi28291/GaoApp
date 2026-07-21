namespace GaoApp.Application.DTOs.Rewards;

public sealed class CreateManualRewardLedgerRequest
{
    public int CustomerId { get; set; }

    // Số tiền tích lũy hợp lệ.
    // Cộng: nhập số dương. Trừ: nhập số âm.
    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
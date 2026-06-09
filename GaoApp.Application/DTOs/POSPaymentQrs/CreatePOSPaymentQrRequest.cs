namespace GaoApp.Application.DTOs.POSPaymentQrs;

public class CreatePOSPaymentQrRequest
{
    public int? BankAccountId { get; set; }

    // Nếu null hoặc <= 0 thì backend lấy số còn thiếu của giỏ hiện tại.
    public decimal? Amount { get; set; }
}
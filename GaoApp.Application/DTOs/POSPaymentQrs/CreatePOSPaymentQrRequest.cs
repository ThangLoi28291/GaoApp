namespace GaoApp.Application.DTOs.POSPaymentQrs;

public class CreatePOSPaymentQrRequest
{
    // Retrying the same create operation must not issue another bank QR.
    public Guid? ClientRequestId { get; set; }
    public int? BankAccountId { get; set; }

    // Nếu null hoặc <= 0 thì backend lấy số còn thiếu của giỏ hiện tại.
    public decimal? Amount { get; set; }
}

namespace GaoApp.Domain.Enums;

public enum BankQrConfirmMode
{
    // Nhân viên tự kiểm tra app ngân hàng rồi bấm xác nhận
    Manual = 0,

    // Provider gọi webhook/callback về hệ thống
    Callback = 1,

    // Hệ thống tự polling API provider
    Polling = 2
}
namespace GaoApp.Domain.Enums;

public enum BankQrRenderMode
{
    // Tự build chuỗi VietQR EMV + tự render QR bằng QRCoder
    LocalEmvQr = 0,

    // Dùng link ảnh VietQR.io, để sau làm nếu cần
    VietQrQuickLink = 1,

    // Dùng provider API động có callback, để giai đoạn sau
    ProviderApi = 2
}
namespace GaoApp.Application.Interfaces.Services.Invoices;

/// <summary>
/// Bảo vệ thông tin xác thực của nhà cung cấp hóa đơn trước khi lưu database.
/// Implementation phải hỗ trợ đọc dữ liệu plaintext cũ để nâng cấp không gián đoạn.
/// </summary>
public interface IInvoiceProviderCredentialProtector
{
    bool IsProtected(string value);

    string Protect(string plaintext);

    string Unprotect(string protectedOrLegacyPlaintext);
}

using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Infrastructure.Services.Invoices;

/// <summary>
/// Mã hóa có xác thực bằng ASP.NET Core Data Protection.
/// Prefix giúp phân biệt bản ghi mới với plaintext legacy.
/// </summary>
public sealed class InvoiceProviderCredentialProtector : IInvoiceProviderCredentialProtector
{
    private const string Prefix = "gdp:v1:";
    private const int DatabaseColumnMaxLength = 500;
    private readonly IDataProtector _protector;

    public InvoiceProviderCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(
            "GaoApp.InvoiceProviderSetting.Password.v1");
    }

    public bool IsProtected(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new InvalidOperationException("Mật khẩu nhà cung cấp không được trống.");

        if (IsProtected(plaintext))
            return plaintext;

        var protectedValue = Prefix + _protector.Protect(plaintext);

        if (protectedValue.Length > DatabaseColumnMaxLength)
        {
            throw new InvalidOperationException(
                "Mật khẩu nhà cung cấp quá dài để lưu an toàn.");
        }

        return protectedValue;
    }

    public string Unprotect(string protectedOrLegacyPlaintext)
    {
        if (string.IsNullOrWhiteSpace(protectedOrLegacyPlaintext))
            return string.Empty;

        // Tương thích dữ liệu cũ: chuỗi không có prefix được xem là plaintext.
        if (!IsProtected(protectedOrLegacyPlaintext))
            return protectedOrLegacyPlaintext;

        return _protector.Unprotect(protectedOrLegacyPlaintext[Prefix.Length..]);
    }
}

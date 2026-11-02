using System.Security.Cryptography;
using GaoApp.Application.Common;

namespace GaoApp.Tests.Security;

public class InvoiceCredentialErrorsTests
{
    [Theory]
    [InlineData(unchecked((int)0x80090016))]
    [InlineData(unchecked((int)0x80090010))]
    [InlineData(unchecked((int)0x80070005))]
    public void Native_key_access_error_inside_wrapper_has_host_repair_guidance(int nativeCode)
    {
        var exception = new CryptographicException("Unable to decrypt payload", new CryptographicException(nativeCode));
        var error = InvoiceCredentialErrors.CannotDecrypt(exception, "Không giải mã được.");
        Assert.Equal(InvoiceCredentialErrors.KeyUnavailableCode, error.Code);
        Assert.Contains("quyền đọc", error.Message);
        Assert.Contains("không cần đổi mật khẩu Viettel", error.Message);
        Assert.DoesNotContain(exception.Message, error.Message);
    }

    [Fact]
    public void Unknown_cryptographic_failure_does_not_claim_original_key_has_been_deleted()
    {
        var error = InvoiceCredentialErrors.CannotDecrypt(new CryptographicException("arbitrary detail"), "Không giải mã được.");
        Assert.Contains("khi đã xác nhận khóa gốc bị mất", error.Message);
        Assert.DoesNotContain("arbitrary detail", error.Message);
    }
}

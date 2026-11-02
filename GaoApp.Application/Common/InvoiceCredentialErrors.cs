using System.Security.Cryptography;
using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Common;

public static class InvoiceCredentialErrors
{
    // Keep the persisted code: existing drafts are reopened only after the
    // worker has successfully decrypted the active credential again.
    public const string KeyUnavailableCode = "InvoiceProvider.CredentialKeyUnavailable";

    public static Error CannotDecrypt(CryptographicException exception, string context)
    {
        var keyAccessFailure = IsKeyAccessFailure(exception);
        var guidance = keyAccessFailure
            ? "Tiến trình này không mở được khóa mã hóa Windows. " +
              "Hãy kiểm tra Web và Worker dùng cùng bộ DataProtection keys; " +
              "khóa DPAPI phải dùng trên máy đã tạo khóa, còn PFX phải đúng certificate/private key " +
              "và tài khoản chạy Worker phải có quyền đọc. " +
              "Nếu Test kết nối trên Web thành công, hãy sửa cấu hình/quyền khóa của Worker; " +
              "không cần đổi mật khẩu Viettel."
            : "Hãy đối chiếu DataProtection:KeysPath và certificate của Web/Worker. " +
              "Chỉ nhập lại mật khẩu rồi bấm Lưu khi đã xác nhận khóa gốc bị mất và không thể khôi phục.";

        return Error.Validation(KeyUnavailableCode,
            $"{context} {guidance} Máy xử lý: {Environment.MachineName}.");
    }

    private static bool IsKeyAccessFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            // NTE_BAD_KEYSET / NTE_PERM / E_ACCESSDENIED. Check the native
            // code as well as the message because Windows can localize it.
            if (current.HResult is unchecked((int)0x80090016) or
                unchecked((int)0x80090010) or unchecked((int)0x80070005) ||
                current is UnauthorizedAccessException ||
                current.Message.Contains("Keyset does not exist", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

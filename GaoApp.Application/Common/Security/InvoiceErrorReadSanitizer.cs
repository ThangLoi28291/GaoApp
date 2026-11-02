using System.Globalization;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Common.Security;

/// <summary>
/// Public invoice diagnostics are a fixed vocabulary, not trusted free text.
/// Apply only when building read models/responses, after business state decisions.
/// Never assign these display values back to persisted entities or retry inputs.
/// </summary>
public static partial class InvoiceErrorReadSanitizer
{
    public const string UnknownCode = "INVOICE_ERROR";
    public const string GenericMessage = "Không hoàn tất được thao tác hóa đơn. Liên hệ quản trị để kiểm tra.";
    public const string GenericWorkerResult = "Thông tin chi tiết của tác vụ được giữ trong nhật ký nội bộ.";
    public const string GenericWarning = "Có cảnh báo về cấu hình hóa đơn. Liên hệ quản trị để kiểm tra.";
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIMEOUT"] = "Kết nối hóa đơn hết thời gian chờ. Cần tra cứu trạng thái trước khi thực hiện tiếp.",
        ["UNKNOWN_RESULT"] = "Kết quả phát hành chưa xác định. Cần tra cứu UUID trước khi thực hiện tiếp.",
        ["HTTP_500"] = "Nhà cung cấp hóa đơn gặp lỗi máy chủ. Cần kiểm tra trạng thái hóa đơn trước khi thực hiện tiếp.",
        ["VIETTEL_SERVER_500"] = "Nhà cung cấp hóa đơn gặp lỗi máy chủ. Cần kiểm tra trạng thái hóa đơn trước khi thực hiện tiếp.",
        ["AUTH_FAILED"] = "Không xác thực được kết nối hóa đơn. Cần kiểm tra cấu hình kết nối.",
        ["UUID_NOT_FOUND"] = "Chưa tìm thấy hóa đơn theo UUID. Cần đối chiếu kết quả tra cứu trước khi thực hiện tiếp.",
        ["Invoice.UnitMissing"] = "Hóa đơn thiếu đơn vị tính. Kiểm tra dòng hàng và kiểm tra lại điều kiện phát hành.",
        ["Invoice.InputInvoiceStockInsufficient"] = "Chưa đủ tồn hóa đơn đầu vào. Kiểm tra tồn và kiểm tra lại điều kiện phát hành.",
        ["Invoice.InputStockInsufficient"] = "Tồn khả dụng đã thay đổi hoặc không đủ để thêm. Kiểm tra số lượng mới nhất của sản phẩm.",
        ["Invoice.PickerWarehouseMissing"] = "Chưa xác định được kho hợp lệ của hóa đơn. Kiểm tra kho của pháp nhân hoặc kho mặc định cửa hàng.",
        [InvoiceCredentialErrors.KeyUnavailableCode] = "Không mở được khóa mã hóa cấu hình hóa đơn. Hãy kiểm tra Web và Worker dùng cùng bộ DataProtection keys và có quyền đọc khóa. Nếu Web kiểm tra kết nối thành công, hãy sửa cấu hình/quyền khóa của Worker; không cần đổi mật khẩu Viettel."
    };

    public static string? Code(string? value)
    {
        if (value is null) return null;
        if (value.Length > 128) return UnknownCode;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (TrustedCodes.TryGetValue(value, out var known)) return known;
        var providerCode = InvoiceLogReadSanitizer.Code(value);
        // Provider codes must be a named public code or a bounded HTTP status;
        // arbitrary numeric strings could themselves be a secret.
        return providerCode is not null && providerCode != InvoiceLogReadSanitizer.Redacted &&
            providerCode.Length > 0 && !providerCode.All(char.IsAsciiDigit) ? providerCode : UnknownCode;
    }

    public static string? Message(string? value, string? code = null)
    {
        if (value is null) return null;
        if (value.Length > 4096) return GenericMessage;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (code is { Length: <= 128 } && Labels.TryGetValue(code, out var label)) return label;
        if (value is GenericMessage or GenericWorkerResult or GenericWarning) return value;
        if (Labels.Values.Contains(value, StringComparer.Ordinal)) return value;
        return TrustedMessages.TryGetValue(value, out var known) ? known : GenericMessage;
    }

    public static string UserMessage(Error? error)
        => UserMessage(error?.Message, error?.Code);

    public static string UserMessage(string? value, string? code = null)
        => value is null || value.Length > 4096 || string.IsNullOrWhiteSpace(value)
            ? GenericMessage : Message(value, code)!;

    public static string WithFallback(string? value, string fallback)
        => value is { Length: > 4096 } ? GenericMessage :
            UserMessage(string.IsNullOrWhiteSpace(value) ? fallback : value);

    public static string? Warning(string? value)
    {
        var message = Message(value);
        return message == GenericMessage ? GenericWarning : message;
    }

    public static string? WorkerResult(string? value)
    {
        if (value is null) return null;
        if (value.Length > 4096) return GenericWorkerResult;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (TrustedMessages.TryGetValue(value, out var known)) return known;
        try
        {
            var match = ReopenedCount().Match(value);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                return $"Đã mở lại {count.ToString(CultureInfo.InvariantCulture)} hóa đơn sau khi xác thực lại credential Viettel.";
        }
        catch (RegexMatchTimeoutException) { return GenericWorkerResult; }
        return GenericWorkerResult;
    }

    [GeneratedRegex(@"\AĐã mở lại ([0-9]{1,9}) hóa đơn sau khi xác thực lại credential Viettel\.\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ReopenedCount();
}

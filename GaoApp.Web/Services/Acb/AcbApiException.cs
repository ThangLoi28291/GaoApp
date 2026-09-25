using System.Text.Json;

namespace GaoApp.Web.Services.Acb;

// Messages contain only application text and allowlisted diagnostic codes, never a bank response body.
public sealed class AcbApiException : InvalidOperationException
{
    internal AcbApiException(string message) : base(message) { }
    public bool BusinessRequestNotSent { get; internal set; }
    public string? ProviderResponseCode { get; internal set; }

    internal static AcbApiException NetworkFailure(HttpRequestException error)
    {
        var socket = error.InnerException is System.Net.Sockets.SocketException inner ? $"; Socket {inner.SocketErrorCode}" : "";
        var hint = error.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => "Không phân giải được tên miền ACB. Kiểm tra DNS/kết nối mạng.",
            HttpRequestError.SecureConnectionError => "Không thiết lập được HTTPS/TLS với ACB. Kiểm tra chứng chỉ, ngày giờ hệ thống và kết nối mạng.",
            HttpRequestError.ConnectionError => "Không mở được kết nối tới ACB. Kiểm tra đường truyền, firewall và quyền kết nối của ứng dụng.",
            _ => "Kết nối tới ACB bị lỗi hoặc gián đoạn. Hãy kiểm tra mạng và trạng thái dịch vụ ACB."
        };
        return new($"Lỗi kết nối ACB ({error.HttpRequestError}{socket}). {hint}");
    }

    internal static AcbApiException TokenFailure(int status, string body)
    {
        var oauth = ReadOAuthError(body);
        var hint = oauth switch
        {
            "invalid_scope" => "Kiểm tra TokenScope đúng theo quyền ACB cấp.",
            "unauthorized_client" => "Ứng dụng chưa được phép xác thực bằng client_credentials; cần đối chiếu quyền ACB cấp.",
            "invalid_client" => "Kiểm tra cặp ClientId/Client secret và đúng môi trường ACB đã cấp.",
            _ when status == 401 => "Kiểm tra cặp ClientId/Client secret và đúng môi trường ACB đã cấp.",
            _ when status == 403 => "Kiểm tra quyền ứng dụng và điều kiện IP kết nối do ACB cấp.",
            _ when status == 404 => "Kiểm tra đường dẫn TokenEndpoint.",
            _ when status == 429 => "ACB giới hạn số lần gọi; hãy chờ trước khi thử lại.",
            _ => "Kiểm tra cấu hình kết nối và quyền của ứng dụng tại ACB."
        };
        return new($"Không lấy được token ACB (HTTP {status}{(oauth == null ? "" : "; OAuth " + oauth)}). {hint}");
    }

    private static string? ReadOAuthError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var value = AcbProtocol.Text(json.RootElement, "error");
            return value is "invalid_client" or "invalid_scope" or "unauthorized_client" or "invalid_request" or
                "unsupported_grant_type" or "invalid_grant" or "access_denied" or "server_error" or "temporarily_unavailable"
                ? value : null;
        }
        catch (JsonException)
        {
            // Non-JSON responses use the existing HTTP-status hint; never expose their body.
            return null;
        }
    }
}

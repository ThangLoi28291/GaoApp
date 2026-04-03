namespace GaoApp.Web.Common.Responses;

/// <summary>
/// Response lỗi chuẩn dùng cho API/AJAX.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Đánh dấu request thất bại.
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Thông điệp hiển thị cho client.
    /// </summary>
    public string Message { get; set; } = "Đã xảy ra lỗi hệ thống.";

    /// <summary>
    /// Mã HTTP status code.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// TraceId để đối chiếu log.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Chỉ nên bật ở môi trường Development.
    /// Không trả chi tiết nội bộ ra Production.
    /// </summary>
    public string? Detail { get; set; }
}
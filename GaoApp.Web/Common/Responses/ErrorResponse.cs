namespace GaoApp.Web.Common.Responses;

/// <summary>
/// Payload lỗi chuẩn trả về cho client.
/// 
/// Giữ các field cũ để tương thích ngược,
/// đồng thời bổ sung các field mới cho POS:
/// - ErrorCode
/// - ActionHint
/// - ErrorType
/// - Metadata
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Luôn false đối với response lỗi.
    /// Giữ lại để tương thích với các đoạn JS/API cũ.
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Message chính hiển thị cho người dùng cuối.
    /// </summary>
    public string Message { get; set; } = "Có lỗi xảy ra.";

    /// <summary>
    /// HTTP status code của response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// TraceId để truy vết log.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Detail kỹ thuật. Chỉ nên hiện trong môi trường dev nếu cần.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Mã lỗi chuẩn, đặc biệt dùng cho frontend POS.
    /// Ví dụ:
    /// - POS_SHIFT_NOT_OPEN
    /// - POS_SHIFT_OPENED_BY_ANOTHER_USER
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Gợi ý thao tác tiếp theo cho người dùng.
    /// Ví dụ:
    /// - "Vui lòng mở ca trước khi thực hiện thao tác này."
    /// - "Hãy xử lý hết đơn giữ trước khi đóng ca."
    /// </summary>
    public string? ActionHint { get; set; }

    /// <summary>
    /// Loại lỗi chuẩn để frontend biết nên toast / modal / banner.
    /// Ví dụ:
    /// - validation
    /// - business_rule
    /// - ownership
    /// - context
    /// - permission
    /// - authentication
    /// - state_conflict
    /// - technical
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Dữ liệu phụ cho frontend.
    /// Có thể chứa:
    /// - heldOrderCount
    /// - shiftCode
    /// - ownerUserName
    /// - canTakeOver
    /// - currentDraftId
    /// ...
    /// </summary>
    public object? Metadata { get; set; }
}
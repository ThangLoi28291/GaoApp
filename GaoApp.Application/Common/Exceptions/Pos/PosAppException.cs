using GaoApp.Application.Common;

namespace GaoApp.Application.Common.Exceptions.Pos;

/// <summary>
/// Exception chuẩn dùng riêng cho POS.
/// 
/// Mục tiêu:
/// - Không còn phụ thuộc vào InvalidOperationException + text tiếng Việt
/// - Mang đủ dữ liệu để Web middleware trả JSON lỗi chuẩn cho UI
/// - Hỗ trợ production UX: message rõ + action hint + metadata
/// 
/// Ví dụ sử dụng:
/// throw new PosAppException(
///     errorCode: PosErrorCodes.ShiftNotOpen,
///     message: "Terminal này chưa mở ca POS.",
///     errorType: PosErrorTypes.BusinessRule,
///     actionHint: "Vui lòng mở ca trước khi thực hiện thao tác này.",
///     metadata: new { terminalId = 5, terminalName = "Thu ngân 1" });
/// </summary>
public class PosAppException : AppException
{
    /// <summary>
    /// Mã lỗi chuẩn của POS, ví dụ:
    /// POS_SHIFT_NOT_OPEN
    /// POS_SHIFT_OPENED_BY_ANOTHER_USER
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Loại lỗi chuẩn:
    /// validation / business_rule / ownership / context / ...
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// Gợi ý hành động tiếp theo cho nhân viên / UI.
    /// Ví dụ:
    /// - "Vui lòng mở ca trước khi thực hiện thao tác này."
    /// - "Hãy xử lý hết các đơn giữ trước khi đóng ca."
    /// </summary>
    public string? ActionHint { get; }

    /// <summary>
    /// Dữ liệu phụ để frontend dùng khi cần mở modal/toast/banner.
    /// Ví dụ:
    /// - heldOrderCount
    /// - shiftId
    /// - shiftCode
    /// - ownerUserName
    /// - canTakeOver
    /// </summary>
    public object? Metadata { get; }

    /// <summary>
    /// HTTP status code gợi ý cho middleware.
    /// 
    /// Không bắt buộc phải set.
    /// Nếu null thì middleware có thể tự map dựa trên ErrorType.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Constructor chính cho POS exception.
    /// </summary>
    /// <param name="errorCode">Mã lỗi POS chuẩn.</param>
    /// <param name="message">Message hiển thị cho người dùng.</param>
    /// <param name="errorType">Loại lỗi POS chuẩn.</param>
    /// <param name="actionHint">Gợi ý thao tác tiếp theo.</param>
    /// <param name="metadata">Dữ liệu phụ cho frontend.</param>
    /// <param name="statusCode">HTTP status code gợi ý.</param>
    /// <param name="innerException">Inner exception nếu có.</param>
    public PosAppException(
        string errorCode,
        string message,
        string errorType,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("Error code không được để trống.", nameof(errorCode));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message không được để trống.", nameof(message));

        if (string.IsNullOrWhiteSpace(errorType))
            throw new ArgumentException("Error type không được để trống.", nameof(errorType));

        ErrorCode = errorCode.Trim();
        ErrorType = errorType.Trim();
        ActionHint = string.IsNullOrWhiteSpace(actionHint) ? null : actionHint.Trim();
        Metadata = metadata;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Factory helper cho lỗi validation.
    /// Dùng khi muốn viết service gọn hơn.
    /// </summary>
    public static PosAppException Validation(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 400)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Validation,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi business rule.
    /// </summary>
    public static PosAppException Business(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 400)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.BusinessRule,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi ownership.
    /// </summary>
    public static PosAppException Ownership(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 409)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Ownership,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi thiếu POS context / terminal / user.
    /// </summary>
    public static PosAppException Context(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 400)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Context,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi permission.
    /// </summary>
    public static PosAppException Permission(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 403)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Permission,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi authentication / session.
    /// </summary>
    public static PosAppException Authentication(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 401)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Authentication,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi state conflict.
    /// </summary>
    public static PosAppException StateConflict(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 409)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.StateConflict,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode);
    }

    /// <summary>
    /// Factory helper cho lỗi technical.
    /// </summary>
    public static PosAppException Technical(
        string errorCode,
        string message,
        string? actionHint = null,
        object? metadata = null,
        int? statusCode = 500,
        Exception? innerException = null)
    {
        return new PosAppException(
            errorCode: errorCode,
            message: message,
            errorType: PosErrorTypes.Technical,
            actionHint: actionHint,
            metadata: metadata,
            statusCode: statusCode,
            innerException: innerException);
    }
}
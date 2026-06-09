using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Web.Common.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Middlewares;

/// <summary>
/// Middleware bắt exception toàn cục và trả JSON lỗi chuẩn cho client.
/// 
/// Đợt nâng cấp này bổ sung hỗ trợ PosAppException để:
/// - trả errorCode
/// - actionHint
/// - errorType
/// - metadata
/// 
/// Nhờ đó frontend POS không cần đoán lỗi bằng text nữa.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Model nội bộ dùng để chuẩn hóa dữ liệu lỗi trước khi serialize ra ErrorResponse.
    /// </summary>
    private sealed class ErrorEnvelope
    {
        public int StatusCode { get; init; }
        public string Message { get; init; } = "Có lỗi xảy ra.";
        public string? ErrorCode { get; init; }
        public string? ActionHint { get; init; }
        public string? ErrorType { get; init; }
        public object? Metadata { get; init; }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var mapped = MapException(exception);

        // Ghi log có cấu trúc hơn một chút để dễ tra production
        if (exception is PosAppException posEx)
        {
            _logger.LogWarning(
                exception,
                "POS exception handled. TraceId={TraceId}; ErrorCode={ErrorCode}; ErrorType={ErrorType}; StatusCode={StatusCode}; Path={Path}",
                traceId,
                posEx.ErrorCode,
                posEx.ErrorType,
                mapped.StatusCode,
                context.Request.Path);
        }
        else
        {
            _logger.LogError(
                exception,
                "Unhandled exception. TraceId={TraceId}; StatusCode={StatusCode}; Path={Path}",
                traceId,
                mapped.StatusCode,
                context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response because the response has already started. TraceId={TraceId}",
                traceId);
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = mapped.StatusCode;

        var payload = new ErrorResponse
        {
            Success = false,
            Message = mapped.Message,
            StatusCode = mapped.StatusCode,
            TraceId = traceId,
            Detail = _environment.IsDevelopment() ? exception.ToString() : null,

            // Field mới cho POS / UI mapping
            ErrorCode = mapped.ErrorCode,
            ActionHint = mapped.ActionHint,
            ErrorType = mapped.ErrorType,
            Metadata = mapped.Metadata
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Map exception sang response lỗi chuẩn.
    /// 
    /// Thứ tự ưu tiên:
    /// 1. PosAppException
    /// 2. AppException có sẵn của hệ thống
    /// 3. Các exception framework/thường gặp
    /// 4. Fallback technical error
    /// </summary>
    private static ErrorEnvelope MapException(Exception exception)
    {
        // =========================================================
        // 1) POS EXCEPTION - Ưu tiên cao nhất cho module POS
        // =========================================================
        if (exception is PosAppException posEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = posEx.StatusCode ?? MapStatusCodeFromPosErrorType(posEx.ErrorType),
                Message = posEx.Message,
                ErrorCode = posEx.ErrorCode,
                ActionHint = posEx.ActionHint,
                ErrorType = posEx.ErrorType,
                Metadata = posEx.Metadata
            };
        }

        // =========================================================
        // 2) APP EXCEPTIONS CÓ SẴN TRONG HỆ THỐNG
        // =========================================================
        if (exception is ValidationAppException validationEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = validationEx.Message,
                ErrorType = PosErrorTypes.Validation
            };
        }

        if (exception is ConflictAppException conflictEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status409Conflict,
                Message = conflictEx.Message,
                ErrorType = PosErrorTypes.StateConflict
            };
        }

        if (exception is ForbiddenAppException forbiddenEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status403Forbidden,
                Message = forbiddenEx.Message,
                ErrorType = PosErrorTypes.Permission
            };
        }

        if (exception is NotFoundAppException notFoundEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message = notFoundEx.Message,
                ErrorType = PosErrorTypes.BusinessRule
            };
        }

        // =========================================================
        // 3) CÁC EXCEPTION THƯỜNG GẶP
        // =========================================================
        if (exception is UnauthorizedAccessException unauthorizedEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                Message = unauthorizedEx.Message,
                ErrorCode = PosErrorCodes.AuthUnauthorized,
                ActionHint = "Vui lòng đăng nhập lại để tiếp tục.",
                ErrorType = PosErrorTypes.Authentication
            };
        }

        if (exception is ArgumentException argumentEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = argumentEx.Message,
                ErrorType = PosErrorTypes.Validation
            };
        }

        if (exception is InvalidOperationException invalidOperationEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = invalidOperationEx.Message,
                ErrorType = PosErrorTypes.BusinessRule
            };
        }

        if (exception is DbUpdateException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                Message = "Không thể lưu dữ liệu vào hệ thống.",
                ActionHint = "Vui lòng thử lại. Nếu lỗi còn tiếp diễn, hãy liên hệ quản lý hoặc kỹ thuật.",
                ErrorType = PosErrorTypes.Technical
            };
        }

        if (exception is JsonException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Dữ liệu gửi lên không hợp lệ.",
                ErrorType = PosErrorTypes.Validation
            };
        }

        if (exception is BadHttpRequestException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Yêu cầu gửi lên không hợp lệ.",
                ErrorType = PosErrorTypes.Validation
            };
        }

        if (exception is OperationCanceledException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status408RequestTimeout,
                Message = "Yêu cầu đã bị hủy hoặc quá thời gian xử lý.",
                ActionHint = "Vui lòng thử lại.",
                ErrorType = PosErrorTypes.Technical
            };
        }

        // =========================================================
        // 4) FALLBACK
        // =========================================================
        return new ErrorEnvelope
        {
            StatusCode = StatusCodes.Status500InternalServerError,
            Message = "Có lỗi hệ thống xảy ra.",
            ActionHint = "Vui lòng thử lại. Nếu lỗi còn tiếp diễn, hãy liên hệ kỹ thuật.",
            ErrorType = PosErrorTypes.Technical
        };
    }

    /// <summary>
    /// Map PosErrorType sang HTTP status code mặc định
    /// khi PosAppException không truyền StatusCode cụ thể.
    /// </summary>
    private static int MapStatusCodeFromPosErrorType(string? errorType)
    {
        return errorType switch
        {
            PosErrorTypes.Validation => StatusCodes.Status400BadRequest,
            PosErrorTypes.BusinessRule => StatusCodes.Status400BadRequest,
            PosErrorTypes.Context => StatusCodes.Status400BadRequest,
            PosErrorTypes.Ownership => StatusCodes.Status409Conflict,
            PosErrorTypes.Permission => StatusCodes.Status403Forbidden,
            PosErrorTypes.Authentication => StatusCodes.Status401Unauthorized,
            PosErrorTypes.StateConflict => StatusCodes.Status409Conflict,
            PosErrorTypes.Technical => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
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

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã đóng kết nối: không ghi response 408 và không log như lỗi hệ thống.
            _logger.LogDebug(
                "Request cancelled by client. TraceId={TraceId}; Path={Path}",
                context.TraceIdentifier,
                context.Request.Path);
            throw;
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(
                    "Cannot write error response because the response has already started. TraceId={TraceId}; Path={Path}; ExceptionType={ExceptionType}",
                    context.TraceIdentifier,
                    context.Request.Path,
                    ex.GetType().Name);
                throw;
            }

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

        if (mapped.StatusCode < StatusCodes.Status500InternalServerError)
        {
            _logger.LogWarning(
                "Expected request failure handled. TraceId={TraceId}; ErrorCode={ErrorCode}; ErrorType={ErrorType}; StatusCode={StatusCode}; Path={Path}; ExceptionType={ExceptionType}",
                traceId,
                mapped.ErrorCode,
                mapped.ErrorType,
                mapped.StatusCode,
                context.Request.Path,
                exception.GetType().Name);
        }
        else
        {
            var diagnostic = SafeExceptionDiagnosticBuilder.Build(exception);
            _logger.LogError(
                "Unhandled exception. TraceId={TraceId}; StatusCode={StatusCode}; Method={Method}; Path={Path}; ExceptionType={ExceptionType}; HResult={HResult}; StackFrames={StackFrames}; InnerExceptionTypes={InnerExceptionTypes}; Fingerprint={Fingerprint}",
                traceId,
                mapped.StatusCode,
                context.Request.Method,
                context.Request.Path,
                diagnostic.ExceptionType,
                diagnostic.HResult,
                diagnostic.StackFrames,
                diagnostic.InnerExceptionTypes,
                diagnostic.Fingerprint);
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
            Detail = null,

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

        if (exception is BusinessRuleException businessRuleEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = businessRuleEx.Message,
                ErrorType = PosErrorTypes.BusinessRule
            };
        }

        if (exception is ConcurrencyException concurrencyEx)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status409Conflict,
                Message = concurrencyEx.Message,
                ErrorType = PosErrorTypes.StateConflict
            };
        }

        // =========================================================
        // 3) CÁC EXCEPTION THƯỜNG GẶP
        // =========================================================
        if (exception is DbUpdateConcurrencyException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status409Conflict,
                Message = "Dữ liệu đã được thay đổi bởi một thao tác khác.",
                ActionHint = "Vui lòng tải lại dữ liệu và thử lại.",
                ErrorType = PosErrorTypes.StateConflict
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

        if (exception is BadHttpRequestException)
        {
            return new ErrorEnvelope
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Yêu cầu gửi lên không hợp lệ.",
                ErrorType = PosErrorTypes.Validation
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

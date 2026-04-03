using System.Text.Json;
using GaoApp.Web.Common.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Middlewares;

/// <summary>
/// Middleware bắt lỗi toàn cục cho toàn bộ request.
/// - Log lỗi thống nhất
/// - Trả JSON cho API/AJAX
/// - Redirect sang trang lỗi cho MVC thông thường
/// - Hỗ trợ phân biệt request Admin hay ngoài Admin để render view phù hợp
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IWebHostEnvironment environment)
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
    /// Xử lý exception theo loại request.
    /// </summary>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var (statusCode, clientMessage) = MapException(exception);

        _logger.LogError(
            exception,
            "Unhandled exception. TraceId={TraceId}, Path={Path}, Method={Method}, StatusCode={StatusCode}",
            traceId,
            context.Request.Path,
            context.Request.Method,
            statusCode);

        // Nếu response đã bắt đầu thì không thể ghi đè nữa
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                exception,
                "Cannot handle exception normally because response has already started. TraceId={TraceId}",
                traceId);

            // Không thể ghi đè response nữa, nên chỉ log rồi kết thúc.
            return;
        }

        context.Response.Clear();

        // Nếu là API/AJAX/JSON request thì trả JSON
        if (IsApiRequest(context))
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new ErrorResponse
            {
                Success = false,
                Message = clientMessage,
                StatusCode = statusCode,
                TraceId = traceId,
                Detail = _environment.IsDevelopment() ? exception.ToString() : null
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
            return;
        }

        // Với request MVC thường thì chuyển sang trang lỗi thân thiện
        // Tránh loop nếu chính /error bị lỗi
        if (context.Request.Path.StartsWithSegments("/error", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";

            var message = _environment.IsDevelopment()
                ? exception.ToString()
                : "Đã xảy ra lỗi hệ thống.";

            await context.Response.WriteAsync(message);
            return;
        }

        var encodedMessage = Uri.EscapeDataString(clientMessage);
        var encodedTraceId = Uri.EscapeDataString(traceId);

        // Xác định request gốc có thuộc admin area hay không
        var isAdminRequest =
            context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) ||
            (context.Request.RouteValues.TryGetValue("area", out var areaValue) &&
             string.Equals(areaValue?.ToString(), "Admin", StringComparison.OrdinalIgnoreCase));

        var encodedIsAdmin = isAdminRequest ? "1" : "0";

        context.Response.Redirect(
            $"/error/{statusCode}?traceId={encodedTraceId}&message={encodedMessage}&isAdmin={encodedIsAdmin}");
    }

    /// <summary>
    /// Mapping exception sang HTTP status code + message trả client.
    /// Có thể mở rộng dần theo domain của GaoApp.
    /// </summary>
    private static (int StatusCode, string Message) MapException(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "Bạn không có quyền thực hiện thao tác này."
            ),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Không tìm thấy dữ liệu yêu cầu."
            ),

            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Dữ liệu đầu vào không hợp lệ."
            ),

            InvalidOperationException => (
                StatusCodes.Status400BadRequest,
                "Thao tác hiện tại không hợp lệ."
            ),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Dữ liệu đã được thay đổi bởi người dùng khác. Vui lòng tải lại trang và thử lại."
            ),

            DbUpdateException => (
                StatusCodes.Status500InternalServerError,
                "Có lỗi khi lưu dữ liệu xuống cơ sở dữ liệu."
            ),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
            )
        };
    }

    /// <summary>
    /// Xác định request có nên trả JSON hay không.
    /// </summary>
    private static bool IsApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Các route API
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            return true;

        // AJAX request
        if (context.Request.Headers.TryGetValue("X-Requested-With", out var requestedWith) &&
            requestedWith == "XMLHttpRequest")
            return true;

        // Client mong đợi JSON
        var accept = context.Request.Headers.Accept.ToString();
        if (!string.IsNullOrWhiteSpace(accept) &&
            accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Content-Type là JSON
        var contentType = context.Request.ContentType;
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
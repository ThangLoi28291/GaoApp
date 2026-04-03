using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

/// <summary>
/// Controller xử lý trang lỗi chung cho toàn hệ thống.
/// Đặt ngoài Area để middleware có thể redirect thống nhất về /error/{statusCode}.
/// </summary>
[AllowAnonymous]
[Route("error")]
public class ErrorController : Controller
{
    [HttpGet("{statusCode:int}")]
    public IActionResult Handle(int statusCode, string? traceId, string? message, string? isAdmin)
    {
        ViewBag.StatusCode = statusCode;
        ViewBag.TraceId = string.IsNullOrWhiteSpace(traceId) ? HttpContext.TraceIdentifier : traceId;
        ViewBag.Message = string.IsNullOrWhiteSpace(message) ? GetDefaultMessage(statusCode) : message;
        ViewBag.IsAdmin = string.Equals(isAdmin, "1", StringComparison.OrdinalIgnoreCase);

        // Admin dùng view riêng để giữ layout / style admin
        if ((bool)ViewBag.IsAdmin)
        {
            return View("~/Areas/Admin/Views/Error/Error.cshtml");
        }

        // Ngoài admin dùng view global
        return View("Error");
    }

    private static string GetDefaultMessage(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status403Forbidden => "Bạn không có quyền truy cập tài nguyên này.",
            StatusCodes.Status404NotFound => "Không tìm thấy trang hoặc dữ liệu yêu cầu.",
            StatusCodes.Status400BadRequest => "Yêu cầu không hợp lệ.",
            _ => "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
        };
    }
}
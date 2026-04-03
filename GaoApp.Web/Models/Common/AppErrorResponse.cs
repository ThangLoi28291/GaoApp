namespace GaoApp.Web.Models.Common;

/// <summary>
/// Response lỗi chuẩn cho API / AJAX / JSON response.
/// </summary>
public class AppErrorResponse
{
    public int Status { get; set; }
    public string Code { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? TraceId { get; set; }
    public object? Errors { get; set; }
}
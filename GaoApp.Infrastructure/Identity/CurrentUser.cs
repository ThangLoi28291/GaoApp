using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace GaoApp.Infrastructure.Identity;

/// <summary>
/// Đọc thông tin user hiện tại từ claims.
/// Chỉ giữ thông tin thuộc session đăng nhập.
/// Store lấy riêng từ ICurrentStore để bám tenant/subdomain.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName
        => _httpContextAccessor.HttpContext?.User?.Identity?.Name;

    public int? TerminalId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("terminal_id");
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string? TerminalCode
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue("terminal_code");

    public bool IsAuthenticated
        => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
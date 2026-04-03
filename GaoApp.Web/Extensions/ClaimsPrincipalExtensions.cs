using System.Security.Claims;

namespace GaoApp.Web.Extensions;

/// <summary>
/// Extension helper đọc các claim thường dùng từ user hiện tại.
/// Hiện tại dùng chủ yếu để lấy UserId.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
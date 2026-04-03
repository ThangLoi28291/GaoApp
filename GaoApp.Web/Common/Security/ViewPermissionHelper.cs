using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Services.Security;

namespace GaoApp.Web.Common.Security;

/// <summary>
/// Helper dùng trong Razor view để kiểm tra quyền hiện tại của user.
/// 
/// Mục đích:
/// - giúp View không phải tự parse claim/store
/// - không query DB trực tiếp trong Razor
/// - chỉ dùng để ẩn/hiện UI
/// 
/// Lưu ý:
/// - backend authorization bằng [Authorize] mới là lớp bảo vệ chính
/// - helper này chỉ phục vụ UI/UX
/// </summary>
public static class ViewPermissionHelper
{
    /// <summary>
    /// Kiểm tra user hiện tại có permission trong tenant/store hiện tại hay không.
    /// 
    /// Rule:
    /// - nếu là host admin => luôn true
    /// - nếu không có StoreId hợp lệ => false
    /// - nếu không lấy được userId từ claim => false
    /// - còn lại gọi CurrentStorePermissionService để kiểm tra
    /// </summary>
    public static async Task<bool> HasPermissionAsync(
        ICurrentStorePermissionService permissionService,
        ITenantContext tenantContext,
        ClaimsPrincipal user,
        string permissionCode,
        CancellationToken ct = default)
    {
        // Host admin được pass tất cả policy ở tầng UI để đồng bộ với AuthorizationHandler.
        if (tenantContext.IsHostAdmin)
            return true;

        // Không resolve được store hiện tại => không xác định được scope permission.
        if (!tenantContext.StoreId.HasValue || tenantContext.StoreId.Value <= 0)
            return false;

        // Lấy userId từ claim.
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
            return false;

        return await permissionService.HasPermissionAsync(
            tenantContext.StoreId.Value,
            userId,
            permissionCode,
            ct);
    }
}
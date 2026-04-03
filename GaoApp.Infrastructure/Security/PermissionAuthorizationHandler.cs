using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Authorization;

namespace GaoApp.Infrastructure.Security;

/// <summary>
/// Authorization handler cho permission policy động.
/// 
/// Flow:
/// 1. kiểm tra user đã đăng nhập
/// 2. lấy userId từ claim
/// 3. nếu là host admin => pass toàn bộ policy
/// 4. nếu là tenant store => kiểm tra permission trong store hiện tại
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentStorePermissionService _permissionService;
    private readonly ITenantContext _tenantContext;

    public PermissionAuthorizationHandler(
        ICurrentStorePermissionService permissionService,
        ITenantContext tenantContext)
    {
        _permissionService = permissionService;
        _tenantContext = tenantContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Chưa đăng nhập => không authorize
        if (context.User?.Identity?.IsAuthenticated != true)
            return;

        // Lấy userId từ claim đăng nhập.
        // Hệ thống hiện tại đang dùng ClaimTypes.NameIdentifier.
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
            return;

        // Host admin là cấp cao nhất ở domain admin/root.
        // Cho pass trực tiếp để không cần map theo từng store.
        if (_tenantContext.IsHostAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        // Nếu không resolve được StoreId thì không thể xác định scope phân quyền.
        if (!_tenantContext.StoreId.HasValue || _tenantContext.StoreId.Value <= 0)
            return;

        // Kiểm tra permission của user trong store hiện tại.
        var hasPermission = await _permissionService.HasPermissionAsync(
            _tenantContext.StoreId.Value,
            userId,
            requirement.PermissionCode);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
using GaoApp.Application.DTOs.Security.Permissions;

namespace GaoApp.Application.Interfaces.Services.Security;

/// <summary>
/// Service use-case cho màn gán permission cho Role.
/// </summary>
public interface IRolePermissionAdminService
{
    /// <summary>
    /// Lấy permission matrix của role thuộc store hiện tại.
    /// </summary>
    Task<RolePermissionMatrixDto?> GetMatrixAsync(int storeId, int roleId, CancellationToken ct = default);

    /// <summary>
    /// Lưu danh sách permission được chọn cho role.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SaveAsync(
        int storeId,
        int? actorUserId,
        SaveRolePermissionsRequest request,
        CancellationToken ct = default);
}
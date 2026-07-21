using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IRolePermissionRepository
{
    /// <summary>
    /// Lấy toàn bộ permission id hiện đang gán cho role.
    /// </summary>
    Task<List<int>> GetPermissionIdsByRoleIdAsync(int roleId, CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ RolePermission của role nếu cần xử lý chi tiết.
    /// </summary>
    Task<List<RolePermission>> GetByRoleIdAsync(int roleId, CancellationToken ct = default);

    /// <summary>
    /// Xóa toàn bộ mapping permission của role.
    /// Chỉ dùng nếu chọn chiến lược replace-all.
    /// </summary>
    Task RemoveAllByRoleIdAsync(int roleId, CancellationToken ct = default);

    /// <summary>
    /// Thêm nhiều mapping permission cho role.
    /// </summary>
    Task AddRangeAsync(IEnumerable<RolePermission> entities, CancellationToken ct = default);

    /// <summary>
    /// Xóa các mapping cụ thể theo role + permission ids.
    /// </summary>
    Task RemoveByRoleIdAndPermissionIdsAsync(
        int roleId,
        IEnumerable<int> permissionIds,
        CancellationToken ct = default);
}
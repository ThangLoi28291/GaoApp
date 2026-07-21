using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.AdminMenus;

public interface IAdminMenuPermissionRepository
{
    Task<List<Permission>> GetPermissionsAsync(CancellationToken ct = default);

    Task<Permission?> GetPermissionByCodeAsync(
        string code,
        CancellationToken ct = default);

    Task<Permission> AddPermissionAsync(
        Permission permission,
        CancellationToken ct = default);

    Task<List<Role>> GetRolesAsync(
        int storeId,
        CancellationToken ct = default);

    Task<List<int>> GetRoleIdsByPermissionIdAsync(
        int storeId,
        int permissionId,
        CancellationToken ct = default);

    Task AddPermissionToRolesAsync(
        int storeId,
        int permissionId,
        List<int> roleIds,
        CancellationToken ct = default);
    Task<List<int>> GetRoleIdsByPermissionCodeAsync(
        int storeId,
        string permissionCode,
        CancellationToken ct = default);

    Task SyncPermissionRolesAsync(
        int storeId,
        Permission permission,
        List<int> roleIds,
        CancellationToken ct = default);
}

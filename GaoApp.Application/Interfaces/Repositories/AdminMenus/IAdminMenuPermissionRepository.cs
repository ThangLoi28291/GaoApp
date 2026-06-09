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
        int permissionId,
        CancellationToken ct = default);

    Task AddPermissionToRolesAsync(
        int permissionId,
        List<int> roleIds,
        CancellationToken ct = default);
    Task<List<int>> GetRoleIdsByPermissionCodeAsync(
    string permissionCode,
    CancellationToken ct = default);

    Task SyncPermissionRolesAsync(
    int permissionId,
    List<int> roleIds,
    CancellationToken ct = default);
}
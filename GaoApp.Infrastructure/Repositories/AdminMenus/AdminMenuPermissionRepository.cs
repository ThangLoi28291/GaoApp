using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.AdminMenus;

public class AdminMenuPermissionRepository : IAdminMenuPermissionRepository
{
    private readonly AppDbContext _db;

    public AdminMenuPermissionRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Permission>> GetPermissionsAsync(CancellationToken ct = default)
    {
        return await _db.Permissions
            .IgnoreQueryFilters()
            .OrderBy(x => x.GroupName)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);
    }

    public async Task<Permission?> GetPermissionByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        return await _db.Permissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Code == code, ct);
    }

    public async Task<Permission> AddPermissionAsync(
        Permission permission,
        CancellationToken ct = default)
    {
        await _db.Permissions.AddAsync(permission, ct);
        return permission;
    }

    public async Task<List<Role>> GetRolesAsync(
        int storeId,
        CancellationToken ct = default)
    {
        return await _db.Roles
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<List<int>> GetRoleIdsByPermissionIdAsync(
        int permissionId,
        CancellationToken ct = default)
    {
        return await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(x => x.PermissionId == permissionId)
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task AddPermissionToRolesAsync(
        int permissionId,
        List<int> roleIds,
        CancellationToken ct = default)
    {
        if (roleIds == null || roleIds.Count == 0)
            return;

        var existingRoleIds = await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(x => x.PermissionId == permissionId && roleIds.Contains(x.RoleId))
            .Select(x => x.RoleId)
            .ToListAsync(ct);

        var needAdd = roleIds
            .Except(existingRoleIds)
            .Select(roleId => new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            })
            .ToList();

        if (needAdd.Count > 0)
            await _db.RolePermissions.AddRangeAsync(needAdd, ct);
    }
    public async Task<List<int>> GetRoleIdsByPermissionCodeAsync(
    string permissionCode,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
            return new List<int>();

        var permission = await _db.Permissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Code == permissionCode,
                ct);

        if (permission == null)
            return new List<int>();

        return await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(x => x.PermissionId == permission.Id)
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }
    public async Task SyncPermissionRolesAsync(
    int permissionId,
    List<int> roleIds,
    CancellationToken ct = default)
    {
        roleIds ??= new List<int>();

        roleIds = roleIds
            .Distinct()
            .ToList();

        var currentItems = await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(x => x.PermissionId == permissionId)
            .ToListAsync(ct);

        var currentRoleIds = currentItems
            .Select(x => x.RoleId)
            .Distinct()
            .ToList();

        var removeItems = currentItems
            .Where(x => !roleIds.Contains(x.RoleId))
            .ToList();

        if (removeItems.Any())
            _db.RolePermissions.RemoveRange(removeItems);

        var addItems = roleIds
            .Where(roleId => !currentRoleIds.Contains(roleId))
            .Select(roleId => new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            })
            .ToList();

        if (addItems.Any())
            await _db.RolePermissions.AddRangeAsync(addItems, ct);
    }
}
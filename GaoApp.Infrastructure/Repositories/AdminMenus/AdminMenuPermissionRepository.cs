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
            .AsNoTracking()
            .OrderBy(x => x.GroupName)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);
    }

    public async Task<Permission?> GetPermissionByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        return await _db.Permissions
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<List<int>> GetRoleIdsByPermissionIdAsync(
        int storeId,
        int permissionId,
        CancellationToken ct = default)
    {
        return await _db.RolePermissions
            .AsNoTracking()
            .Where(x =>
                x.PermissionId == permissionId &&
                x.Role.StoreId == storeId &&
                !x.Role.IsDeleted)
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task AddPermissionToRolesAsync(
        int storeId,
        int permissionId,
        List<int> roleIds,
        CancellationToken ct = default)
    {
        if (roleIds == null || roleIds.Count == 0)
            return;

        var validRoleIds = await ValidateRoleIdsAsync(storeId, roleIds, ct);

        var existingRoleIds = await _db.RolePermissions
            .Where(x =>
                x.PermissionId == permissionId &&
                x.Role.StoreId == storeId &&
                validRoleIds.Contains(x.RoleId))
            .Select(x => x.RoleId)
            .ToListAsync(ct);

        var needAdd = validRoleIds
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
        int storeId,
        string permissionCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
            return new List<int>();

        var permission = await _db.Permissions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Code == permissionCode,
                ct);

        if (permission == null)
            return new List<int>();

        return await _db.RolePermissions
            .AsNoTracking()
            .Where(x =>
                x.PermissionId == permission.Id &&
                x.Role.StoreId == storeId &&
                !x.Role.IsDeleted)
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }
    public async Task SyncPermissionRolesAsync(
        int storeId,
        Permission permission,
        List<int> roleIds,
        CancellationToken ct = default)
    {
        roleIds ??= new List<int>();

        roleIds = await ValidateRoleIdsAsync(storeId, roleIds, ct);

        var currentItems = permission.Id > 0
            ? await _db.RolePermissions
                .Where(x =>
                    x.PermissionId == permission.Id &&
                    x.Role.StoreId == storeId &&
                    !x.Role.IsDeleted)
                .ToListAsync(ct)
            : new List<RolePermission>();

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
            .Select(roleId => permission.Id > 0
                ? new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permission.Id
                }
                : new RolePermission
                {
                    RoleId = roleId,
                    Permission = permission
                })
            .ToList();

        if (addItems.Any())
            await _db.RolePermissions.AddRangeAsync(addItems, ct);
    }

    private async Task<List<int>> ValidateRoleIdsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct)
    {
        var requestedRoleIds = roleIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (requestedRoleIds.Count == 0)
            return requestedRoleIds;

        var validRoleIds = await _db.Roles
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                requestedRoleIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (validRoleIds.Count != requestedRoleIds.Count)
        {
            throw new InvalidOperationException(
                "Danh sách vai trò có phần tử không thuộc cửa hàng hiện tại.");
        }

        return validRoleIds;
    }
}

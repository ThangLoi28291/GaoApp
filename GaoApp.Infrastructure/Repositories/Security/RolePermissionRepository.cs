using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly AppDbContext _db;

    public RolePermissionRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<int>> GetPermissionIdsByRoleIdAsync(int roleId, CancellationToken ct = default)
    {
        return _db.RolePermissions
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);
    }

    public Task<List<RolePermission>> GetByRoleIdAsync(int roleId, CancellationToken ct = default)
    {
        return _db.RolePermissions
            .Where(x => x.RoleId == roleId)
            .ToListAsync(ct);
    }

    public async Task RemoveAllByRoleIdAsync(int roleId, CancellationToken ct = default)
    {
        var entities = await _db.RolePermissions
            .Where(x => x.RoleId == roleId)
            .ToListAsync(ct);

        _db.RolePermissions.RemoveRange(entities);
    }

    public Task AddRangeAsync(IEnumerable<RolePermission> entities, CancellationToken ct = default)
    {
        _db.RolePermissions.AddRange(entities);
        return Task.CompletedTask;
    }

    public async Task RemoveByRoleIdAndPermissionIdsAsync(
        int roleId,
        IEnumerable<int> permissionIds,
        CancellationToken ct = default)
    {
        var ids = permissionIds.Distinct().ToList();

        if (ids.Count == 0) return;

        var entities = await _db.RolePermissions
            .Where(x => x.RoleId == roleId && ids.Contains(x.PermissionId))
            .ToListAsync(ct);

        _db.RolePermissions.RemoveRange(entities);
    }
}
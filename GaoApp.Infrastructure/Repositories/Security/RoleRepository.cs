using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;

    public RoleRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(List<Role> Items, int Total)> GetPagedAsync(
        int storeId,
        string? keyword,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Roles.Where(x => x.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x => x.Name.Contains(keyword) || x.Code.Contains(keyword));
        }

        if (isActive.HasValue)
        {
            if (isActive.Value)
                query = query.Where(x => !x.IsDeleted);
            else
                query = query.Where(x => x.IsDeleted);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Role?> GetByIdAsync(int storeId, int roleId, CancellationToken ct = default)
    {
        return _db.Roles.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == roleId, ct);
    }

    public Task<bool> ExistsNameAsync(int storeId, string name, int? ignoreId = null, CancellationToken ct = default)
    {
        return _db.Roles.AnyAsync(x =>
            x.StoreId == storeId &&
            !x.IsDeleted &&
            x.Name == name &&
            (!ignoreId.HasValue || x.Id != ignoreId.Value), ct);
    }

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? ignoreId = null, CancellationToken ct = default)
    {
        return _db.Roles.AnyAsync(x =>
            x.StoreId == storeId &&
            !x.IsDeleted &&
            x.Code == code &&
            (!ignoreId.HasValue || x.Id != ignoreId.Value), ct);
    }

    public Task AddAsync(Role entity, CancellationToken ct = default)
    {
        _db.Roles.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Role entity, CancellationToken ct = default)
    {
        _db.Roles.Update(entity);
        return Task.CompletedTask;
    }

    public async Task<bool> SoftDeleteAsync(int storeId, int roleId, int? actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == roleId && !x.IsDeleted, ct);
        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedBy = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        return true;
    }

    public Task<List<Role>> GetLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default)
    {
        var query = _db.Roles.Where(x => x.StoreId == storeId);

        if (onlyActive)
            query = query.Where(x => !x.IsDeleted);

        return query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<Dictionary<int, int>> GetPermissionCountsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct = default)
    {
        var ids = roleIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<int, int>();

        return await _db.RolePermissions
            .Where(x => ids.Contains(x.RoleId) && x.Role.StoreId == storeId && !x.Role.IsDeleted)
            .GroupBy(x => x.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);
    }

    public async Task<Dictionary<int, int>> GetUserCountsAsync(
        int storeId,
        IEnumerable<int> roleIds,
        CancellationToken ct = default)
    {
        var ids = roleIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<int, int>();

        return await _db.UserInStores
            .Where(x => x.StoreId == storeId && ids.Contains(x.RoleId) && !x.IsDeleted)
            .GroupBy(x => x.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);
    }
}
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public class UserInStoreRepository : IUserInStoreRepository
{
    private readonly AppDbContext _db;

    public UserInStoreRepository(AppDbContext db)
    {
        _db = db;
    }


    public async Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
    int storeId,
    int userId,
    CancellationToken ct = default)
    {
        return await _db.UserInStores
            .Where(x => x.StoreId == storeId && x.UserId == userId && x.IsActive && !x.IsDeleted)
            .SelectMany(x => x.Role.RolePermissions.Select(rp => rp.Permission.Code))
            .Distinct()
            .ToListAsync(ct);
    }
    public async Task<(List<UserInStore> Items, int Total)> GetPagedAsync(
        int storeId,
        string? keyword,
        int? roleId,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.UserInStores
            .Include(x => x.User)
            .Include(x => x.Role)
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x =>
                x.User.UserName.Contains(keyword) ||
                (x.User.FullName != null && x.User.FullName.Contains(keyword)) ||
                (x.User.Email != null && x.User.Email.Contains(keyword)));
        }

        if (roleId.HasValue)
            query = query.Where(x => x.RoleId == roleId.Value);

        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(x => x.User.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<UserInStore?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
    {
        return _db.UserInStores
            .Include(x => x.User)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);
    }

    public Task<bool> ExistsAsync(int storeId, int userId, CancellationToken ct = default)
    {
        return _db.UserInStores
            .AnyAsync(x => x.StoreId == storeId && x.UserId == userId && !x.IsDeleted, ct);
    }

    public Task AddAsync(UserInStore entity, CancellationToken ct = default)
    {
        _db.UserInStores.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserInStore entity, CancellationToken ct = default)
    {
        _db.UserInStores.Update(entity);
        return Task.CompletedTask;
    }

    public async Task<bool> SoftDeleteAsync(int storeId, int id, int? actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.UserInStores
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);

        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedBy = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorUserId;

        return true;
    }

    public Task<int> CountByRoleIdAsync(int storeId, int roleId, CancellationToken ct = default)
    {
        return _db.UserInStores
            .CountAsync(x => x.StoreId == storeId && x.RoleId == roleId && !x.IsDeleted, ct);
    }

    public async Task<Dictionary<int, int>> CountByRoleIdsAsync(
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
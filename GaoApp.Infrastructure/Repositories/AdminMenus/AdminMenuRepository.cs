// GaoApp.Infrastructure/Repositories/AdminMenus/AdminMenuRepository.cs
using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.AdminMenus;

public class AdminMenuRepository : IAdminMenuRepository
{
    private readonly AppDbContext _db;

    public AdminMenuRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AdminMenuItem>> GetAllForStoreAsync(
        int storeId,
        CancellationToken ct = default)
    {
        return await _db.AdminMenuItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
    }

    public async Task<AdminMenuItem?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return await _db.AdminMenuItems
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                !x.IsDeleted &&
                x.StoreId == storeId,
                ct);
    }

    public async Task AddAsync(
        AdminMenuItem entity,
        CancellationToken ct = default)
    {
        await _db.AdminMenuItems.AddAsync(entity, ct);
    }

    public async Task<bool> HasChildrenAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return await _db.AdminMenuItems
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.ParentId == id &&
                !x.IsDeleted,
                ct);
    }
}

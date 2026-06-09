using GaoApp.Application.Interfaces.Repositories.Display;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Display;

public class DisplayPromotionRepository : IDisplayPromotionRepository
{
    private readonly AppDbContext _db;

    public DisplayPromotionRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<DisplayPromotion>> GetActiveForCustomerDisplayAsync(
        int storeId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return _db.DisplayPromotions
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.IsActive &&
                (x.StartAt == null || x.StartAt <= now) &&
                (x.EndAt == null || x.EndAt >= now))
       .OrderByDescending(x => x.Priority)
.ThenBy(x => x.SortOrder)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<DisplayPromotion>> GetListAsync(
        int storeId,
        CancellationToken cancellationToken = default)
    {
        return _db.DisplayPromotions
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
           .OrderByDescending(x => x.Priority)
.ThenBy(x => x.SortOrder)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<DisplayPromotion?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken cancellationToken = default)
    {
        return _db.DisplayPromotions
            .FirstOrDefaultAsync(
                x => x.StoreId == storeId && x.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        DisplayPromotion entity,
        CancellationToken cancellationToken = default)
    {
        await _db.DisplayPromotions.AddAsync(entity, cancellationToken);
    }

    public void Update(DisplayPromotion entity)
    {
        _db.DisplayPromotions.Update(entity);
    }

    public void Remove(DisplayPromotion entity)
    {
        _db.DisplayPromotions.Remove(entity);
    }
    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}
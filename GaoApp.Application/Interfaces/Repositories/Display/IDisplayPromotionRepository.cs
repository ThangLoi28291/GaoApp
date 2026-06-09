using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Display;

public interface IDisplayPromotionRepository
{
    Task<List<DisplayPromotion>> GetActiveForCustomerDisplayAsync(
        int storeId,
        DateTime now,
        CancellationToken ct = default);

    Task<List<DisplayPromotion>> GetListAsync(
        int storeId,
        CancellationToken ct = default);

    Task<DisplayPromotion?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task AddAsync(
        DisplayPromotion entity,
        CancellationToken ct = default);

    void Update(DisplayPromotion entity);

    void Remove(DisplayPromotion entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}
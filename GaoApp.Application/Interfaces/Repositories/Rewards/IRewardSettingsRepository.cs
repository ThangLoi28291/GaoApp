using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface IRewardSettingsRepository
{
    Task<List<Category>> GetCategoriesForAdminAsync(int storeId, CancellationToken ct = default);
    /// <summary>
    /// Cấu hình hiện tại của Store, kể cả khi IsEnabled = false.
    /// Tenant/global Store filter vẫn là boundary mặc định.
    /// </summary>
    Task<RewardSettings?> GetCurrentAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Admin read/write path. Trả tracked entity để update.
    /// Không lọc IsEnabled.
    /// </summary>
    Task<RewardSettings?> GetForAdminAsync(
        int storeId,
        CancellationToken ct = default);

    Task AddAsync(
        RewardSettings settings,
        CancellationToken ct = default);

    Task SaveChangesAsync(
        CancellationToken ct = default);
}

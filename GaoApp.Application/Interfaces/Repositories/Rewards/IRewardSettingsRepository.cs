using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface IRewardSettingsRepository
{
    Task<RewardSettings?> GetCurrentAsync(CancellationToken ct = default);

    Task AddAsync(RewardSettings settings, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
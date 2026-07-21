using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class RewardSettingsRepository : IRewardSettingsRepository
{
    private readonly AppDbContext _db;

    public RewardSettingsRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RewardSettings?> GetCurrentAsync(CancellationToken ct = default)
    {
        return await _db.RewardSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsEnabled, ct);
    }

    public async Task AddAsync(RewardSettings settings, CancellationToken ct = default)
    {
        await _db.RewardSettings.AddAsync(settings, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}
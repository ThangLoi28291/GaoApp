
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Media;

public sealed class MediaAssetRepository : IMediaAssetRepository
{
    private readonly AppDbContext _db;
    public MediaAssetRepository(AppDbContext db) => _db = db;

    public Task AddAsync(MediaAsset asset, CancellationToken ct = default)
        => _db.MediaAssets.AddAsync(asset, ct).AsTask();

    public Task<MediaAsset?> GetTempByTokenAsync(string token, int storeId, CancellationToken ct = default)
        => _db.MediaAssets.FirstOrDefaultAsync(x =>
            x.StoreId == storeId && !x.IsDeleted && x.IsTemp && x.TempToken == token, ct);

    public Task<List<MediaAsset>> GetTempsByTokensAsync(List<string> tokens, int storeId, CancellationToken ct = default)
        => _db.MediaAssets
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.IsTemp && x.TempToken != null &&
                x.ExpireAtUtc > DateTime.UtcNow && x.StoragePath.StartsWith("uploads/products/") && tokens.Contains(x.TempToken))
            .ToListAsync(ct);

    public void Update(MediaAsset asset) => _db.MediaAssets.Update(asset);
    public void Remove(MediaAsset asset) => _db.MediaAssets.Remove(asset);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

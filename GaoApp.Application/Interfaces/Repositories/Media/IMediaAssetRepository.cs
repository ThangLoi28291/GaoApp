using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Media;

public interface IMediaAssetRepository
{
    Task AddAsync(MediaAsset asset, CancellationToken ct = default);
    Task<MediaAsset?> GetTempByTokenAsync(string token, int storeId, CancellationToken ct = default);
    Task<List<MediaAsset>> GetTempsByTokensAsync(List<string> tokens, int storeId, CancellationToken ct = default);

    void Update(MediaAsset asset);
    void Remove(MediaAsset asset);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

}

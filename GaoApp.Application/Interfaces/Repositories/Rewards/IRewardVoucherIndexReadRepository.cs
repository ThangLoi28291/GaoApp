using GaoApp.Application.DTOs.Rewards.Vouchers;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface IRewardVoucherIndexReadRepository
{
    Task<RewardVoucherIndexPageDto> QueryAsync(int storeId, RewardVoucherIndexQueryRequest request, CancellationToken ct = default);
    Task<RewardVoucherIndexQuickViewDto?> GetQuickViewAsync(int storeId, int voucherId, CancellationToken ct = default);
}

using GaoApp.Application.DTOs.Rewards.Vouchers;

namespace GaoApp.Application.Interfaces.Services.Rewards;

public interface IRewardVoucherIndexReadService
{
    Task<RewardVoucherIndexPageDto> GetPageAsync(int storeId, RewardVoucherIndexQueryRequest request, CancellationToken ct = default);
    Task<RewardVoucherIndexQuickViewDto?> GetQuickViewAsync(int storeId, int voucherId, CancellationToken ct = default);
}

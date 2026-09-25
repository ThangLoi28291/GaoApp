using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Rewards;

namespace GaoApp.Application.Interfaces.Services.Rewards;

public interface IRewardSettingsAdminService
{
    Task<RewardSettingsAdminDto> GetAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Result<RewardSettingsAdminDto>> SaveAsync(
        int storeId,
        SaveRewardSettingsRequest request,
        CancellationToken ct = default);
}
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Rewards;

namespace GaoApp.Application.Services.Rewards;

public sealed class RewardVoucherIndexReadService : IRewardVoucherIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private readonly IRewardVoucherIndexReadRepository _repository;

    public RewardVoucherIndexReadService(IRewardVoucherIndexReadRepository repository)
    {
        _repository = repository;
    }

    public Task<RewardVoucherIndexPageDto> GetPageAsync(int storeId, RewardVoucherIndexQueryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureStore(storeId);

        var normalized = new RewardVoucherIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 150),
            Status = NormalizeStatus(request.Status),
            FromDate = request.FromDate?.Date,
            ToDate = request.ToDate?.Date,
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20
        };

        return _repository.QueryAsync(storeId, normalized, ct);
    }

    public Task<RewardVoucherIndexQuickViewDto?> GetQuickViewAsync(int storeId, int voucherId, CancellationToken ct = default)
    {
        EnsureStore(storeId);
        if (voucherId <= 0)
            throw new InvalidOperationException("Phiếu giảm giá không hợp lệ.");

        return _repository.GetQuickViewAsync(storeId, voucherId, ct);
    }

    private static void EnsureStore(int storeId)
    {
        if (storeId <= 0)
            throw new InvalidOperationException("Không xác định được cửa hàng hiện tại.");
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeStatus(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            RewardVoucherIndexStatuses.Available => RewardVoucherIndexStatuses.Available,
            RewardVoucherIndexStatuses.Used => RewardVoucherIndexStatuses.Used,
            RewardVoucherIndexStatuses.Cancelled => RewardVoucherIndexStatuses.Cancelled,
            RewardVoucherIndexStatuses.Expired => RewardVoucherIndexStatuses.Expired,
            RewardVoucherIndexStatuses.Locked => RewardVoucherIndexStatuses.Locked,
            _ => RewardVoucherIndexStatuses.All
        };
}

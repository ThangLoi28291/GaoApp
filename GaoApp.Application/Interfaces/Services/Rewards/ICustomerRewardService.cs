using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.DTOs.Rewards.Vouchers;

namespace GaoApp.Application.Interfaces.Services.Rewards;

public interface ICustomerRewardService
{
    Task<CustomerRewardBalanceDto> GetBalanceAsync(int customerId, CancellationToken ct = default);
    Task<CustomerRewardBalanceDto> CreateManualLedgerAsync(
    CreateManualRewardLedgerRequest request,
    CancellationToken ct = default);
    Task<List<CustomerRewardVoucherDto>> GetAvailableVouchersAsync(
    int customerId,
    CancellationToken ct = default);
    Task<RedeemRewardVoucherResultDto> RedeemVoucherAsync(
    RedeemRewardVoucherRequest request,
    CancellationToken ct = default);
    Task<CustomerRewardSummaryDto> GetSummaryAsync(
    int customerId,
    CancellationToken ct = default);
    Task<PagedResult<CustomerRewardVoucherListItemDto>> GetVoucherListAsync(
    CustomerRewardVoucherFilterDto filter,
    CancellationToken ct = default);

    Task<CustomerRewardVoucherDetailDto> GetVoucherDetailAsync(
        int id,
        CancellationToken ct = default);

    Task<CustomerRewardVoucherDetailDto> CancelVoucherAsync(
        int id,
        CancelCustomerRewardVoucherRequest request,
        CancellationToken ct = default);

    Task<Dictionary<string, int>> GetVoucherStatusCountsAsync(
        CancellationToken ct = default);
    Task<CustomerRewardVoucherDetailDto> LockVoucherAsync(
    int id,
    LockCustomerRewardVoucherRequest request,
    CancellationToken ct = default);

    Task<CustomerRewardVoucherDetailDto> UnlockVoucherAsync(
        int id,
        UnlockCustomerRewardVoucherRequest request,
        CancellationToken ct = default);

    Task<List<CustomerRewardVoucherLogDto>> GetVoucherLogsAsync(
        int id,
        CancellationToken ct = default);

    Task<CustomerRewardVoucherPrintDto> GetVoucherPrintAsync(
        int id,
        CancellationToken ct = default);
    Task<CustomerRewardVoucherDetailDto> LookupVoucherByCodeAsync(
    string code,
    CancellationToken ct = default);
}
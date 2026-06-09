using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface ICustomerRewardVoucherRepository
{
    Task<List<CustomerRewardVoucher>> GetByCustomerAsync(
        int customerId,
        CustomerRewardVoucherStatus? status = null,
        CancellationToken ct = default);

    Task<int> CountAvailableAsync(int customerId, CancellationToken ct = default);

    Task AddAsync(CustomerRewardVoucher voucher, CancellationToken ct = default);
    Task<string> GenerateNextVoucherCodeAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<PagedResult<CustomerRewardVoucherListItemDto>> GetListAsync(
    CustomerRewardVoucherFilterDto filter,
    CancellationToken ct = default);

    Task<CustomerRewardVoucher?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<CustomerRewardVoucherDetailDto?> GetDetailAsync(
        int id,
        CancellationToken ct = default);

    Task<Dictionary<CustomerRewardVoucherStatus, int>> CountByStatusAsync(
        CancellationToken ct = default);
    Task RestoreUsedVouchersByOrderIdAsync(
    int orderId,
    string reason,
    CancellationToken ct = default);
    Task<CustomerRewardVoucher?> GetByCodeAsync(
    string code,
    CancellationToken ct = default);
}
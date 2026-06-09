using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface ICustomerRewardLedgerRepository
{
    Task<decimal> GetBalanceAmountAsync(int customerId, CancellationToken ct = default);

    Task<bool> HasLedgerForOrderAsync(int orderId, CancellationToken ct = default);

    Task<bool> HasLedgerForSalesReturnAsync(int salesReturnId, CancellationToken ct = default);

    Task AddAsync(CustomerRewardLedger ledger, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> HasLedgerForOrderAsync(
    int orderId,
    CustomerRewardLedgerType type,
    CancellationToken ct = default);

    Task<decimal> GetOrderLedgerAmountAsync(
        int orderId,
        CustomerRewardLedgerType type,
        CancellationToken ct = default);
    Task<bool> HasLedgerForSalesReturnAsync(
    int salesReturnId,
    CustomerRewardLedgerType type,
    CancellationToken ct = default);
}
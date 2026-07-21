using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class CustomerRewardLedgerRepository : ICustomerRewardLedgerRepository
{
    private readonly AppDbContext _db;

    public CustomerRewardLedgerRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetBalanceAmountAsync(int customerId, CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .Where(x => x.CustomerId == customerId)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    }

    public async Task<bool> HasLedgerForOrderAsync(int orderId, CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .AnyAsync(x => x.OrderId == orderId, ct);
    }

    public async Task<bool> HasLedgerForSalesReturnAsync(int salesReturnId, CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .AnyAsync(x => x.SalesReturnId == salesReturnId, ct);
    }

    public async Task AddAsync(CustomerRewardLedger ledger, CancellationToken ct = default)
    {
        await _db.CustomerRewardLedgers.AddAsync(ledger, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<bool> HasLedgerForOrderAsync(
    int orderId,
    CustomerRewardLedgerType type,
    CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .AnyAsync(x => x.OrderId == orderId && x.Type == type, ct);
    }

    public async Task<decimal> GetOrderLedgerAmountAsync(
        int orderId,
        CustomerRewardLedgerType type,
        CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .Where(x => x.OrderId == orderId && x.Type == type)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    }
    public async Task<bool> HasLedgerForSalesReturnAsync(
    int salesReturnId,
    CustomerRewardLedgerType type,
    CancellationToken ct = default)
    {
        return await _db.CustomerRewardLedgers
            .AnyAsync(x => x.SalesReturnId == salesReturnId && x.Type == type, ct);
    }
}
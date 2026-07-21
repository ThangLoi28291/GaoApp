using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class OrderLegalEntityAllocationReversalRepository
    : IOrderLegalEntityAllocationReversalRepository
{
    private readonly AppDbContext _db;

    public OrderLegalEntityAllocationReversalRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<int, decimal>> GetReversedBaseQuantityBySourceEntryIdsAsync(
        IReadOnlyCollection<int> sourceValuationEntryIds,
        CancellationToken ct = default)
    {
        var ids = (sourceValuationEntryIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<int, decimal>();

        return await _db.OrderLegalEntityAllocationReversals
            .Where(x => ids.Contains(x.SourceValuationEntryId) && !x.IsDeleted)
            .GroupBy(x => x.SourceValuationEntryId)
            .Select(x => new { SourceId = x.Key, Quantity = x.Sum(y => y.BaseQuantity) })
            .ToDictionaryAsync(x => x.SourceId, x => x.Quantity, ct);
    }

    public Task<List<OrderLegalEntityAllocationReversal>> GetForOrderAsync(
        int orderId,
        CancellationToken ct = default)
        => _db.OrderLegalEntityAllocationReversals
            .AsNoTracking()
            .Include(x => x.OrderLegalEntityAllocation)
            .Where(x => x.OrderId == orderId && !x.IsDeleted)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public Task AddRangeAsync(
        IReadOnlyCollection<OrderLegalEntityAllocationReversal> reversals,
        CancellationToken ct = default)
    {
        if (reversals == null || reversals.Count == 0)
            return Task.CompletedTask;

        return _db.OrderLegalEntityAllocationReversals.AddRangeAsync(reversals, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

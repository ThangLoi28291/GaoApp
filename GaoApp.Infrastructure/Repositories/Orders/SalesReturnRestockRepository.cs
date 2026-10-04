using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class SalesReturnRestockRepository(AppDbContext db) : ISalesReturnRestockRepository
{
    private bool? schemaAvailable;
    private async Task<bool> IsAvailableAsync(CancellationToken ct)
        => schemaAvailable ??= await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.SalesReturnRestockFragments', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
    public async Task EnsureAvailableAsync(CancellationToken ct)
    {
        if (!await IsAvailableAsync(ct)) throw new GaoApp.Application.Common.Exceptions.BusinessRuleException("Cần cập nhật cơ sở dữ liệu cho chức năng hàng trả chờ nhập kho.");
    }
    public async Task LockOrderAsync(int orderId, CancellationToken ct)
    {
        var storeId = db.CurrentStoreId ?? throw new InvalidOperationException("A store is required.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {orderId} AND [StoreId] = {storeId}", ct);
    }
    public Task AddRangeAsync(IReadOnlyCollection<SalesReturnRestockFragment> rows, CancellationToken ct)
        => db.SalesReturnRestockFragments.AddRangeAsync(rows, ct);
    public Task<bool> HasFragmentsAsync(int returnId, CancellationToken ct)
        => db.SalesReturnRestockFragments.AnyAsync(x => x.SalesReturnLine.SalesReturnId == returnId && !x.IsDeleted, ct);
    public Task<List<OrderLegalEntityAllocationReversal>> GetLegalReservationsAsync(int returnId, CancellationToken ct)
        => db.OrderLegalEntityAllocationReversals.Where(x => x.SalesReturnId == returnId && x.ReversalType == OrderLegalEntityReversalType.ReturnPendingRestock && !x.IsDeleted).ToListAsync(ct);
    public async Task<List<SalesReturnRestockFragment>> GetPendingAsync(int? returnId, CancellationToken ct)
    {
        await EnsureAvailableAsync(ct);
        return await db.SalesReturnRestockFragments
            .Include(x => x.SalesReturnLine).ThenInclude(x => x.SalesReturn).ThenInclude(x => x.Order)
            .Include(x => x.SourceValuationEntry).ThenInclude(x => x.Warehouse)
            .Include(x => x.AllocationReversal)
            .Where(x => !x.IsDeleted && x.CompletedAtUtc == null && !x.SalesReturnLine.IsDeleted && !x.SalesReturnLine.SalesReturn.IsDeleted && x.SalesReturnLine.SalesReturn.Status == SalesReturnStatus.Completed && (returnId == null || x.SalesReturnLine.SalesReturnId == returnId))
            .OrderBy(x => x.SalesReturnLine.SalesReturnId).ThenBy(x => x.Id).ToListAsync(ct);
    }
    public async Task<Dictionary<int, decimal>> GetLegacyReservedQuantitiesAsync(IReadOnlyCollection<int> sourceIds, CancellationToken ct)
    {
        // Older databases can continue normal returns until the additive migration is applied.
        if (sourceIds.Count == 0 || !await IsAvailableAsync(ct)) return [];
        return await db.SalesReturnRestockFragments.Where(x => sourceIds.Contains(x.SourceValuationEntryId) && !x.IsDeleted && x.CompletedAtUtc == null && x.AllocationReversalId == null)
            .GroupBy(x => x.SourceValuationEntryId).Select(x => new {Id = x.Key, Qty = x.Sum(y => y.BaseQuantity)}).ToDictionaryAsync(x => x.Id, x => x.Qty, ct);
    }
}

using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Orders;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed partial class OrderInventoryIssueRepository
{
    private Task<bool> NativeIssueLine(int id, CancellationToken ct) => _db.OrderInventoryIssueLines.AnyAsync(x => x.Id == id && x.OrderLine.StorefrontOrderLineId != null, ct);
    private async Task<decimal> NativeAvailable(int id, CancellationToken ct)
    {
        var keys = await (from s in _db.Set<StorefrontInventoryIssueSource>() join e in _db.InventoryValuationEntries on s.InventoryValuationEntryId equals e.Id
            where s.OrderInventoryIssueLineId == id select new { e.WarehouseId, e.ProductVariantId }).Distinct().ToListAsync(ct);
        decimal total = 0;
        foreach (var key in keys) total += await _db.InventoryBalances.Where(x => x.WarehouseId == key.WarehouseId && x.ProductVariantId == key.ProductVariantId).SumAsync(x => x.OnHandQty - x.ReservedQty, ct);
        return total;
    }
    private async Task<InventoryIssueCostResolutionSnapshotDto> NativeCost(int orderId, int lineId, CancellationToken ct)
    {
        var ids = await _db.OrderLegalEntityAllocations.AsNoTracking().Where(x => x.OrderId == orderId && x.OrderLineId == lineId && x.InventoryTransactionId != null)
            .Select(x => x.InventoryTransactionId!.Value).ToListAsync(ct);
        var graph = await StorefrontValuationEvidence.ReadAsync(_db, ids, ct); var results = graph.Roots.Select(graph.Evaluate).ToList();
        return new() { HasRevaluationEntry = results.Count > 0 && results.All(x => x.State == SaleValuationCostPolicy.Quality.Finalized),
            RevaluationAmount = graph.Events.Sum(x => x.Amount), LastRevaluationAtUtc = graph.Events.Select(x => (DateTime?)x.OccurredAtUtc)
                .Concat(graph.Roots.SelectMany(x => x.CostLayerAllocations).Select(x => x.ResolvedAtUtc)).Max() };
    }
    private async Task<List<InventoryIssueDocumentOptionDto>> NativeOptions(int lineId, InventoryReferenceType type, CancellationToken ct)
    {
        var issue = await _db.OrderInventoryIssueLines.AsNoTracking().Where(x => x.Id == lineId).Select(x => x.OrderInventoryIssueId).SingleAsync(ct);
        return (await NativeInbound(issue, ct)).Where(x => x.SourceReferenceType == type && x.SourceReferenceId > 0)
            .GroupBy(x => x.SourceReferenceId).Select(x => new InventoryIssueDocumentOptionDto { Id = x.Key,
                Text = $"Chứng từ #{x.Key} | {x.Sum(y => y.QuantityChange):0.####} đơn vị gốc theo ngân sách nguồn giao" }).ToList();
    }
    private async Task<List<InventoryIssueInboundCandidateDto>> NativeInbound(int issueId, CancellationToken ct)
    {
        var sources = await _db.Set<StorefrontInventoryIssueSource>().AsNoTracking().Where(x => x.OrderInventoryIssueId == issueId).ToListAsync(ct);
        var allocationIds = sources.Select(x => x.InventoryCostLayerAllocationId).ToList();
        var debits = await _db.InventoryCostBudgetDebits.AsNoTracking().Include(x => x.Layer).ThenInclude(x => x.InventoryTransaction)
            .Where(x => allocationIds.Contains(x.InventoryCostLayerAllocationId) && x.IsProvisionalResolution).OrderBy(x => x.Id).Take(100001).ToListAsync(ct);
        if (debits.Count > 100000) throw new ConflictAppException("Nguồn giải quyết giá vốn quá phân mảnh; cần đối soát trước.");
        var result = new List<InventoryIssueInboundCandidateDto>();
        foreach (var debit in debits)
        {
            var source = sources.Single(x => x.InventoryCostLayerAllocationId == debit.InventoryCostLayerAllocationId);
            if (debit.ResolutionStartQuantity is not decimal start || debit.ResolutionEndQuantity is not decimal end || debit.Layer?.InventoryTransaction is not { } inbound)
                throw new ConflictAppException("Khoảng nguồn giải quyết giá vốn thiếu chứng từ nhập thực tế.");
            var quantity = Math.Min(end, source.BaselineResolvedQuantity + source.UnresolvedQuantity) - Math.Max(start, source.BaselineResolvedQuantity);
            if (quantity <= 0) continue;
            result.Add(new() { InventoryTransactionId = inbound.Id, WarehouseId = inbound.WarehouseId, ProductVariantId = inbound.ProductVariantId,
                QuantityChange = quantity, OccurredAtUtc = inbound.OccurredAtUtc, SourceReferenceType = inbound.ReferenceType,
                SourceReferenceId = int.TryParse(inbound.ReferenceId, out var id) ? id : 0, SourceReferenceLineId = inbound.ReferenceLineId,
                Note = "Đúng nguồn ngân sách giải quyết xuất giao; không suy từ kho ca chốt." });
        }
        return result;
    }
}

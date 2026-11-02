using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Orders;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontInventoryIssueAdapter(AppDbContext db, IInventoryBalanceRepository balances, ICurrentUser user,
    ICurrentStorePermissionService permissions, TimeProvider clock) : IStorefrontInventoryIssueAdapter
{
    public async Task<bool> TryRefreshAsync(int issueId, CancellationToken ct = default)
        => await Refresh(issueId, ct) is not null;
    public Task<InventoryIssueApprovalCheckResultDto?> TryValidateAsync(int issueId, CancellationToken ct = default) => Refresh(issueId, ct);
    private async Task<InventoryIssueApprovalCheckResultDto?> Refresh(int issueId, CancellationToken ct)
    {
        var store = db.CurrentStoreId ?? throw new ForbiddenAppException("Cần chọn cửa hàng.");
        var hint = await db.OrderInventoryIssues.AsNoTracking().Where(x => x.StoreId == store && x.Id == issueId).Select(x => (int?)x.OrderId).SingleOrDefaultAsync(ct);
        if (hint is null) return null;
        var binding = await db.Set<StorefrontSaleBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.SaleOrderId == hint, ct); if (binding is null) return null;
        if (!user.IsAuthenticated || user.UserId is not > 0 || !await db.UserInStores.AnyAsync(x => x.StoreId == store && x.UserId == user.UserId && x.IsActive && x.User.IsActive, ct) ||
            !(await permissions.HasPermissionAsync(store, user.UserId.Value, StorefrontPermissions.Reconcile, ct) ||
              await permissions.HasPermissionAsync(store, user.UserId.Value, PermissionCodes.Inventory.Issue.Reconcile, ct) ||
              await permissions.HasPermissionAsync(store, user.UserId.Value, PermissionCodes.Inventory.Issue.Approve, ct)))
            throw new ForbiddenAppException("Đối soát nguồn giá vốn website cần quyền đối soát/duyệt tồn kho.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await StorefrontFulfillmentService.Lock(db, $"GaoApp:Storefront:Cost:{store}:{binding.StorefrontOrderId}", ct);
        var issue = await db.OrderInventoryIssues.Include(x => x.Lines).Include(x => x.Order).ThenInclude(x => x.Lines)
            .SingleAsync(x => x.StoreId == store && x.Id == issueId, ct);
        var maps = await db.OrderLegalEntityAllocations.AsNoTracking().Where(x => x.StoreId == store && x.OrderId == issue.OrderId).ToListAsync(ct);
        await balances.LockBatchAsync(maps.Select(x => new InventoryPostingLockKey(store, x.WarehouseId, x.ProductVariantId)).Distinct().ToList(), ct);
        var graph = await StorefrontValuationEvidence.ReadAsync(db, maps.Select(x => x.InventoryTransactionId ?? throw new ConflictAppException("Thiếu nguồn xuất giao.")), ct);
        var sourceRows = await db.Set<StorefrontInventoryIssueSource>().AsNoTracking().Where(x => x.StoreId == store && x.OrderInventoryIssueId == issueId).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime; var output = new InventoryIssueApprovalCheckResultDto { IssueId = issue.Id, IssueCode = issue.Code };
        var changed = false;
        foreach (var line in issue.Lines.Where(x => !x.IsDeleted))
        {
            var saleLine = issue.Order.Lines.Single(x => x.Id == line.OrderLineId && x.StorefrontOrderId == binding.StorefrontOrderId);
            var ownMaps = maps.Where(x => x.OrderLineId == saleLine.Id).ToList(); var ownTx = ownMaps.Select(x => x.InventoryTransactionId).ToHashSet();
            var roots = graph.Roots.Where(x => ownTx.Contains(x.InventoryTransactionId)).ToList(); var results = roots.Select(graph.Evaluate).ToList();
            var pending = sourceRows.Where(x => x.OrderInventoryIssueLineId == line.Id).ToList();
            if (pending.Count == 0 || roots.Sum(x => -x.Quantity) != saleLine.BaseQuantity || pending.Sum(x => x.UnresolvedQuantity) != line.NegativeQty)
                throw new ConflictAppException("Issue thiếu nguyên nguồn tạm tính đã chốt; không suy từ kho của ca ghi bán.");
            decimal covered = 0;
            foreach (var source in pending)
            {
                var root = roots.Single(x => x.Id == source.InventoryValuationEntryId); var allocation = root.CostLayerAllocations.Single(x => x.Id == source.InventoryCostLayerAllocationId);
                if (!allocation.IsProvisional || source.BaselineResolvedQuantity + source.UnresolvedQuantity != allocation.Quantity || allocation.ResolvedQuantity < source.BaselineResolvedQuantity)
                    throw new ConflictAppException("Khoảng giá vốn tạm tính đã đổi nguồn.");
                covered += Math.Min(source.UnresolvedQuantity, allocation.ResolvedQuantity - source.BaselineResolvedQuantity);
            }
            var final = results.Count > 0 && results.All(x => x.State == SaleValuationCostPolicy.Quality.Finalized && x.Cost.HasValue);
            var known = results.All(x => x.Cost.HasValue && x.State is not (SaleValuationCostPolicy.Quality.DataIntegrityConflict or SaleValuationCostPolicy.Quality.Unavailable));
            var resolved = final && covered == line.NegativeQty;
            var cost = known ? decimal.Round(results.Sum(x => x.Cost!.Value), 2, MidpointRounding.AwayFromZero) : saleLine.LineCostTotal;
            changed |= line.IsResolved != resolved || line.AutoDetectedInboundQty != covered || saleLine.LineCostTotal != cost || saleLine.IsProvisionalCost == final;
            line.AutoDetectedInboundQty = covered; line.AutoDetectedDocumentResolved = covered == line.NegativeQty;
            line.AutoDetectedCostResolved = final; line.IsResolved = resolved; line.ResolvedAtUtc = resolved ? line.ResolvedAtUtc ?? now : null;
            line.LastAutoResolvedAtUtc = now; line.AutoResolveNote = "Đối chiếu trực tiếp nguồn Delivery11 và các khoảng ngân sách giải quyết; không dùng kho của ca chốt.";
            line.AutoDetectedRevaluationAmount = graph.Events.Where(x => roots.Any(r => x.RevaluationOfEntryId == r.Id)).Sum(x => x.Amount);
            line.RevaluationAmount = line.AutoDetectedRevaluationAmount;
            if (known)
            {
                saleLine.LineCostTotal = cost; saleLine.UnitCostSnapshot = decimal.Round(cost!.Value / saleLine.BaseQuantity, 2, MidpointRounding.AwayFromZero);
                saleLine.GrossProfit = ownMaps.Sum(x => x.NetAmount) - cost; saleLine.IsProvisionalCost = !final; saleLine.ProvisionalUnitCost = final ? null : saleLine.UnitCostSnapshot;
            }
            output.Lines.Add(new() { IssueLineId = line.Id, OrderLineId = line.OrderLineId, ProductId = line.ProductId, ProductVariantId = line.ProductVariantId,
                ProductName = saleLine.ItemName, OrderedQty = line.OrderedQty, NegativeQty = line.NegativeQty, QuantityResolved = covered == line.NegativeQty,
                DocumentResolved = covered == line.NegativeQty, CostResolved = final, IsResolved = resolved, CurrentAvailableQty = covered,
                RevaluationAmount = line.RevaluationAmount, ProvisionalCostAmount = line.ProvisionalCostAmount,
                Reasons = resolved ? [] : ["Nguồn xuất thực tế còn khoảng lượng/giá vốn chưa được giải quyết đầy đủ."] });
        }
        output.TotalLines = output.Lines.Count; output.ResolvedLines = output.Lines.Count(x => x.IsResolved); output.UnresolvedLines = output.TotalLines - output.ResolvedLines;
        output.CanApprove = output.TotalLines > 0 && output.UnresolvedLines == 0;
        if (issue.Status is not (InventoryResolutionStatus.Approved or InventoryResolutionStatus.Rejected))
        {
            var status = output.CanApprove ? InventoryResolutionStatus.ReadyForApproval : InventoryResolutionStatus.PendingResolution;
            changed |= issue.Status != status; issue.Status = status; issue.ReadyForApprovalAtUtc = output.CanApprove ? issue.ReadyForApprovalAtUtc ?? now : null;
        }
        issue.AutoResolvedLineCount = output.ResolvedLines; issue.LastAutoResolvedAtUtc = now; issue.RefreshOverdueState(now);
        issue.Order.HasInventoryIssue = issue.Status != InventoryResolutionStatus.None;
        issue.Order.InventoryResolutionStatus = issue.Status; issue.Order.InventoryIssueOpenedAtUtc = issue.OpenedAtUtc; issue.Order.InventoryIssueApprovedAtUtc = issue.ApprovedAtUtc;
        if (changed) db.OrderInventoryIssueActions.Add(new() { StoreId = store, OrderInventoryIssueId = issue.Id, ActorUserId = user.UserId,
            ActionAtUtc = now, ActionType = InventoryIssueActionType.AutoResolutionRefreshed, ReferenceType = InventoryIssueReferenceType.Order,
            ReferenceId = issue.OrderId, Note = "Đối soát nguồn giá vốn xuất giao website; không phát sinh xuất kho hoặc khoản thu mới." });
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct); return output;
    }
}

using GaoApp.Application.DTOs.Orders;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class OrderInventoryIssueRepository : IOrderInventoryIssueRepository
{
    private readonly AppDbContext _db;

    public OrderInventoryIssueRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<OrderInventoryIssue?> GetByIdAsync(int id, CancellationToken ct = default)
        => _db.OrderInventoryIssues
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public async Task<OrderInventoryIssue?> GetDetailByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        var issue = await _db.OrderInventoryIssues
            .Include(x => x.Order)

            // Không filter trực tiếp trong Include để tránh lỗi/khó debug
            .Include(x => x.Lines)
                .ThenInclude(l => l.Product)

            .Include(x => x.Lines)
                .ThenInclude(l => l.ProductVariant)

            .Include(x => x.Actions)
                .ThenInclude(a => a.ActorUser)

            .Include(x => x.ApprovedByUser)
            .Include(x => x.RejectedByUser)

            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

        if (issue == null)
            return null;

        // Filter sau khi load để chỉ giữ dữ liệu còn hiệu lực
        issue.Lines = issue.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        issue.Actions = issue.Actions
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ActionAtUtc)
            .ToList();

        return issue;
    }

    public Task<OrderInventoryIssue?> GetByOrderIdAsync(int orderId, CancellationToken ct = default)
        => _db.OrderInventoryIssues
            .FirstOrDefaultAsync(x => x.OrderId == orderId && !x.IsDeleted, ct);

    public async Task<List<OrderInventoryIssue>> GetListAsync(
        InventoryResolutionStatus? status = null,
        bool? onlyOverdue = null,
        CancellationToken ct = default)
    {
        var query = _db.OrderInventoryIssues
            .AsNoTracking()
            .Include(x => x.Order)
            .Include(x => x.Lines.Where(l => !l.IsDeleted))
            .Where(x => !x.IsDeleted)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (onlyOverdue.HasValue)
        {
            query = query.Where(x => x.IsOverdue == onlyOverdue.Value);
        }

        // UI quản lý nên nhìn thấy case overdue trước.
        // Trong nhóm overdue thì case overdue lâu hơn lên trước.
        // Sau đó tới case mở cũ hơn.
        return await query
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.OverdueSinceUtc ?? DateTime.MaxValue)
            .ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.OpenedAtUtc)
            .ToListAsync(ct);
    }

    public Task<List<OrderInventoryIssue>> GetPendingOrReadyIssuesAsync(CancellationToken ct = default)
        => _db.OrderInventoryIssues
            .Where(x =>
                !x.IsDeleted &&
                (
                    x.Status == InventoryResolutionStatus.PendingResolution ||
                    x.Status == InventoryResolutionStatus.ReadyForApproval
                ))
            .OrderBy(x => x.DueAtUtc)
            .ThenBy(x => x.OpenedAtUtc)
            .ToListAsync(ct);

    public Task<List<OrderInventoryIssue>> GetOverdueCandidatesAsync(DateTime nowUtc, CancellationToken ct = default)
        => _db.OrderInventoryIssues
            .Where(x =>
                !x.IsDeleted &&
                (
                    x.Status == InventoryResolutionStatus.PendingResolution ||
                    x.Status == InventoryResolutionStatus.ReadyForApproval
                ) &&
                x.DueAtUtc <= nowUtc)
            .OrderBy(x => x.DueAtUtc)
            .ThenBy(x => x.OpenedAtUtc)
            .ToListAsync(ct);

    public Task AddAsync(OrderInventoryIssue entity, CancellationToken ct = default)
        => _db.OrderInventoryIssues.AddAsync(entity, ct).AsTask();

    public void Update(OrderInventoryIssue entity)
        => _db.OrderInventoryIssues.Update(entity);

    public Task AddActionAsync(OrderInventoryIssueAction entity, CancellationToken ct = default)
        => _db.OrderInventoryIssueActions.AddAsync(entity, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<decimal> GetCurrentAvailableQtyForIssueLineAsync(int issueLineId, CancellationToken ct = default)
    {
        var row = await (
            from line in _db.OrderInventoryIssueLines
            join issue in _db.OrderInventoryIssues on line.OrderInventoryIssueId equals issue.Id
            join ord in _db.Orders on issue.OrderId equals ord.Id
            join shift in _db.POSShifts on ord.POSShiftId equals shift.Id
            join bal in _db.InventoryBalances
                on new { WarehouseId = shift.WarehouseId, line.ProductVariantId }
                equals new { bal.WarehouseId, bal.ProductVariantId }
                into balanceJoin
            from bal in balanceJoin.DefaultIfEmpty()
            where line.Id == issueLineId && !line.IsDeleted
            select new
            {
                AvailableQty = bal == null ? 0m : (bal.OnHandQty - bal.ReservedQty)
            }
        ).FirstOrDefaultAsync(ct);

        return row?.AvailableQty ?? 0m;
    }

    public async Task<InventoryIssueCostResolutionSnapshotDto> GetCostResolutionSnapshotForOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        // 1) Đọc các provisional allocations gốc của đúng order line
        var allocationRows = await (
            from entry in _db.InventoryValuationEntries
            join allocation in _db.InventoryCostLayerAllocations
                on entry.Id equals allocation.InventoryValuationEntryId
            where !entry.IsDeleted
                  && !allocation.IsDeleted
                  && entry.ReferenceType == InventoryReferenceType.Order
                  && entry.ReferenceId == orderId.ToString()
                  && entry.ReferenceLineId == orderLineId
                  && entry.EntryType == InventoryValuationEntryType.Outbound
                  && entry.IsProvisional
                  && allocation.IsProvisional
            select new
            {
                allocation.Quantity,
                allocation.UnitCost,
                allocation.ResolvedQuantity,
                allocation.ResolvedAmount,
                allocation.ResolvedAtUtc
            })
            .ToListAsync(ct);

        // 2) Nếu order line này không có provisional allocation
        // => không cần revaluation, coi như cost đã ổn
        if (allocationRows.Count == 0)
        {
            return new InventoryIssueCostResolutionSnapshotDto
            {
                HasRevaluationEntry = true,
                RevaluationAmount = 0m,
                LastRevaluationAtUtc = null
            };
        }

        var totalQty = allocationRows.Sum(x => x.Quantity);
        var totalResolvedQty = allocationRows.Sum(x => x.ResolvedQuantity);

        var totalProvisionalAmountOfResolvedPart = allocationRows.Sum(x =>
            Math.Round(x.ResolvedQuantity * x.UnitCost, 4, MidpointRounding.AwayFromZero));

        var totalActualResolvedAmount = allocationRows.Sum(x => x.ResolvedAmount);

        var derivedRevaluationAmount = Math.Round(
            totalActualResolvedAmount - totalProvisionalAmountOfResolvedPart,
            4,
            MidpointRounding.AwayFromZero);

        var lastResolvedAtUtc = allocationRows
            .Where(x => x.ResolvedAtUtc.HasValue)
            .Select(x => x.ResolvedAtUtc)
            .OrderByDescending(x => x)
            .FirstOrDefault();

        // 3) Kiểm tra xem line này đã finalize cost hoàn toàn chưa
        var fullyResolved = totalQty > 0 && totalResolvedQty >= totalQty;

        // 4) Đọc thêm revaluation entry thật để audit / tương thích cũ
        var revaluationRows = await _db.InventoryValuationEntries
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.ReferenceType == InventoryReferenceType.Order &&
                x.ReferenceId == orderId.ToString() &&
                x.ReferenceLineId == orderLineId &&
                x.EntryType == InventoryValuationEntryType.Revaluation)
            .Select(x => new
            {
                x.Amount,
                x.OccurredAtUtc
            })
            .ToListAsync(ct);

        var hasRealRevaluationEntry = revaluationRows.Count > 0;
        var realRevaluationAmount = revaluationRows.Sum(x => x.Amount);
        var lastRealRevaluationAtUtc = revaluationRows.Count > 0
            ? revaluationRows.Max(x => x.OccurredAtUtc)
            : (DateTime?)null;

        return new InventoryIssueCostResolutionSnapshotDto
        {
            HasRevaluationEntry = hasRealRevaluationEntry || fullyResolved,
            RevaluationAmount = hasRealRevaluationEntry
                ? realRevaluationAmount
                : derivedRevaluationAmount,
            LastRevaluationAtUtc = lastRealRevaluationAtUtc ?? lastResolvedAtUtc
        };
    }

    public async Task<List<InventoryIssueDocumentOptionDto>> GetReceiptOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default)
    {
        var issueLine = await (
            from line in _db.OrderInventoryIssueLines
            join issue in _db.OrderInventoryIssues on line.OrderInventoryIssueId equals issue.Id
            join ord in _db.Orders on issue.OrderId equals ord.Id
            join shift in _db.POSShifts on ord.POSShiftId equals shift.Id
            where line.Id == issueLineId && !line.IsDeleted
            select new
            {
                shift.WarehouseId,
                line.ProductVariantId
            }
        ).FirstOrDefaultAsync(ct);

        if (issueLine == null)
            return new List<InventoryIssueDocumentOptionDto>();

        var rows = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.WarehouseId == issueLine.WarehouseId &&
                x.ProductVariantId == issueLine.ProductVariantId &&
                x.QuantityChange > 0 &&
                x.ReferenceType == InventoryReferenceType.StockDocument &&
                x.ReferenceId != null)
            .GroupBy(x => x.ReferenceId)
            .Select(g => new
            {
                ReferenceId = g.Key!,
                Qty = g.Sum(x => x.QuantityChange),
                LastOccurredAtUtc = g.Max(x => x.OccurredAtUtc),
                Note = g
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .Select(x => x.Note)
                    .FirstOrDefault()
            })
            .OrderByDescending(x => x.LastOccurredAtUtc)
            .ToListAsync(ct);

        return rows
            .Select(x => new InventoryIssueDocumentOptionDto
            {
                Id = int.TryParse(x.ReferenceId, out var id) ? id : 0,
                Text = $"{(string.IsNullOrWhiteSpace(x.Note) ? $"Phiếu nhập #{x.ReferenceId}" : x.Note)} | +{x.Qty:N0}"
            })
            .Where(x => x.Id > 0)
            .ToList();
    }

    public async Task<List<InventoryIssueDocumentOptionDto>> GetAdjustmentOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default)
    {
        var issueLine = await (
            from line in _db.OrderInventoryIssueLines
            join issue in _db.OrderInventoryIssues on line.OrderInventoryIssueId equals issue.Id
            join ord in _db.Orders on issue.OrderId equals ord.Id
            join shift in _db.POSShifts on ord.POSShiftId equals shift.Id
            where line.Id == issueLineId && !line.IsDeleted
            select new
            {
                shift.WarehouseId,
                line.ProductVariantId
            }
        ).FirstOrDefaultAsync(ct);

        if (issueLine == null)
            return new List<InventoryIssueDocumentOptionDto>();

        var rows = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.WarehouseId == issueLine.WarehouseId &&
                x.ProductVariantId == issueLine.ProductVariantId &&
                x.QuantityChange > 0 &&
                x.ReferenceType == InventoryReferenceType.Adjustment &&
                x.ReferenceId != null)
            .GroupBy(x => x.ReferenceId)
            .Select(g => new
            {
                ReferenceId = g.Key!,
                Qty = g.Sum(x => x.QuantityChange),
                LastOccurredAtUtc = g.Max(x => x.OccurredAtUtc),
                Note = g
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .Select(x => x.Note)
                    .FirstOrDefault()
            })
            .OrderByDescending(x => x.LastOccurredAtUtc)
            .ToListAsync(ct);

        return rows
            .Select(x => new InventoryIssueDocumentOptionDto
            {
                Id = int.TryParse(x.ReferenceId, out var id) ? id : 0,
                Text = $"{(string.IsNullOrWhiteSpace(x.Note) ? $"Adjustment #{x.ReferenceId}" : x.Note)} | +{x.Qty:N0}"
            })
            .Where(x => x.Id > 0)
            .ToList();
    }

    public async Task<List<InventoryIssueInboundCandidateDto>> GetInboundCandidatesForIssueAsync(
        int issueId,
        CancellationToken ct = default)
    {
        var issueContext = await (
            from issue in _db.OrderInventoryIssues
            join ord in _db.Orders on issue.OrderId equals ord.Id
            join shift in _db.POSShifts on ord.POSShiftId equals shift.Id
            where issue.Id == issueId && !issue.IsDeleted
            select new
            {
                IssueId = issue.Id,
                issue.OpenedAtUtc,
                shift.WarehouseId
            })
            .FirstOrDefaultAsync(ct);

        if (issueContext == null)
            return new List<InventoryIssueInboundCandidateDto>();

        var issueLines = await _db.OrderInventoryIssueLines
            .AsNoTracking()
            .Where(x => x.OrderInventoryIssueId == issueId && !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.ProductVariantId
            })
            .ToListAsync(ct);

        if (issueLines.Count == 0)
            return new List<InventoryIssueInboundCandidateDto>();

        var variantIds = issueLines
            .Select(x => x.ProductVariantId)
            .Distinct()
            .ToList();

        var inboundRows = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.WarehouseId == issueContext.WarehouseId &&
                variantIds.Contains(x.ProductVariantId) &&
                x.QuantityChange > 0 &&
                (
                    x.TransactionType == InventoryTransactionType.PurchaseReceipt ||
                    x.TransactionType == InventoryTransactionType.AdjustmentIncrease ||
                    x.TransactionType == InventoryTransactionType.TransferIn ||
                    x.TransactionType == InventoryTransactionType.StockCountGain ||
                    x.TransactionType == InventoryTransactionType.CustomerReturnIn ||
                    x.TransactionType == InventoryTransactionType.SaleVoidIn ||
                    x.TransactionType == InventoryTransactionType.SaleReturn
                ))
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                InventoryTransactionId = x.Id,
                x.WarehouseId,
                x.ProductVariantId,
                x.TransactionType,
                x.ReferenceType,
                x.ReferenceId,
                x.ReferenceLineId,
                x.QuantityChange,
                x.OccurredAtUtc,
                x.Note
            })
            .ToListAsync(ct);

        static InventoryReferenceType ResolveReferenceType(
            InventoryReferenceType currentReferenceType,
            InventoryTransactionType transactionType)
        {
            if (currentReferenceType != InventoryReferenceType.None)
                return currentReferenceType;

            return transactionType switch
            {
                InventoryTransactionType.PurchaseReceipt => InventoryReferenceType.StockDocument,
                InventoryTransactionType.AdjustmentIncrease => InventoryReferenceType.Adjustment,
                InventoryTransactionType.TransferIn => InventoryReferenceType.StockTransfer,
                InventoryTransactionType.StockCountGain => InventoryReferenceType.StockCount,
                InventoryTransactionType.CustomerReturnIn => InventoryReferenceType.Refund,
                InventoryTransactionType.SaleVoidIn => InventoryReferenceType.Order,
                InventoryTransactionType.SaleReturn => InventoryReferenceType.Refund,
                _ => InventoryReferenceType.None
            };
        }

        static int ParseReferenceId(string? rawReferenceId)
        {
            if (string.IsNullOrWhiteSpace(rawReferenceId))
                return 0;

            return int.TryParse(rawReferenceId, out var id) ? id : 0;
        }

        return inboundRows
            .Select(x => new InventoryIssueInboundCandidateDto
            {
                InventoryTransactionId = x.InventoryTransactionId,
                WarehouseId = x.WarehouseId,
                ProductVariantId = x.ProductVariantId,
                SourceReferenceType = ResolveReferenceType(x.ReferenceType, x.TransactionType),
                SourceReferenceId = ParseReferenceId(x.ReferenceId),
                SourceReferenceLineId = x.ReferenceLineId,
                QuantityChange = x.QuantityChange,
                OccurredAtUtc = x.OccurredAtUtc,
                Note = x.Note
            })
            .ToList();
    }

    public Task<List<OrderInventoryIssueLineAllocation>> GetAllocationsByIssueAsync(
        int issueId,
        CancellationToken ct = default)
    {
        return _db.OrderInventoryIssueLineAllocations
            .Where(x => x.OrderInventoryIssueId == issueId && !x.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task ReplaceAllocationsAsync(
        int issueId,
        List<OrderInventoryIssueLineAllocation> allocations,
        CancellationToken ct = default)
    {
        var oldRows = await _db.OrderInventoryIssueLineAllocations
            .Where(x => x.OrderInventoryIssueId == issueId && !x.IsDeleted)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var row in oldRows)
        {
            row.IsDeleted = true;
            row.DeletedAtUtc = now;
        }

        if (allocations.Count > 0)
            await _db.OrderInventoryIssueLineAllocations.AddRangeAsync(allocations, ct);
    }
}
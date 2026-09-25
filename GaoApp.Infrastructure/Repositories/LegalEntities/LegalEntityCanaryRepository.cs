using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.LegalEntities;

public sealed class LegalEntityCanaryRepository : ILegalEntityCanaryRepository
{
    private readonly AppDbContext _db;

    public LegalEntityCanaryRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Store?> GetStoreAsync(
        int storeId,
        bool forUpdate,
        CancellationToken ct = default)
    {
        var query = _db.Stores.Where(x => x.Id == storeId && !x.IsDeleted);
        return (forUpdate ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }

    public Task<List<LegalEntityActivationEvent>> GetRecentEventsAsync(
        int storeId,
        int take,
        CancellationToken ct = default)
        => _db.LegalEntityActivationEvents
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);

    public async Task<LegalEntityCanaryOperationalMetricsDto> GetOperationalMetricsAsync(
        int storeId,
        DateTime sinceUtc,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        var allocatedOrders = _db.Orders
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.LegalEntityAllocatedAtUtc.HasValue &&
                x.LegalEntityAllocatedAtUtc.Value >= sinceUtc &&
                x.LegalEntityAllocatedAtUtc.Value <= nowUtc);

        var openIssueStatuses = new[]
        {
            InventoryResolutionStatus.PendingResolution,
            InventoryResolutionStatus.ReadyForApproval
        };

        return new LegalEntityCanaryOperationalMetricsDto
        {
            AllocatedOrderCount = await allocatedOrders.CountAsync(ct),
            SplitOrderCount = await allocatedOrders.CountAsync(x => x.HasMultipleLegalEntities, ct),
            OpenInventoryIssueCount = await _db.OrderInventoryIssues
                .AsNoTracking()
                .CountAsync(x =>
                    x.StoreId == storeId &&
                    !x.IsDeleted &&
                    x.OpenedAtUtc >= sinceUtc &&
                    openIssueStatuses.Contains(x.Status),
                    ct),
            FailedInvoiceCount = await _db.InvoiceHeads
                .AsNoTracking()
                .CountAsync(x =>
                    x.StoreId == storeId &&
                    !x.IsDeleted &&
                    x.OriginalInvoiceHeadId == null &&
                    x.Order != null && x.Order.LegalEntityAllocatedAtUtc.HasValue &&
                    x.Order.LegalEntityAllocatedAtUtc.Value >= sinceUtc &&
                    x.ProviderStatus == InvoiceProviderStatus.IssueFailed,
                    ct),
            StaleReservationCount = await _db.InventoryReservations
                .AsNoTracking()
                .CountAsync(x =>
                    x.StoreId == storeId &&
                    !x.IsDeleted &&
                    x.Status == InventoryReservationStatus.Active &&
                    x.ReservedAtUtc < nowUtc.AddHours(-24),
                    ct),
            PendingMultiModeOrderCount = await _db.Orders
                .AsNoTracking()
                .CountAsync(x =>
                    x.StoreId == storeId &&
                    !x.IsDeleted &&
                    x.LegalEntityModeCapturedAtUtc.HasValue &&
                    x.UseMultiLegalEntity &&
                    (x.Status == OrderStatus.Draft || x.Status == OrderStatus.OnHold),
                    ct),
            PendingLegacyModeOrderCount = await _db.Orders
                .AsNoTracking()
                .CountAsync(x =>
                    x.StoreId == storeId &&
                    !x.IsDeleted &&
                    x.LegalEntityModeCapturedAtUtc.HasValue &&
                    !x.UseMultiLegalEntity &&
                    (x.Status == OrderStatus.Draft || x.Status == OrderStatus.OnHold),
                    ct)
        };
    }

    public async Task<(int AllocationMismatchCount, int InvoiceMismatchCount)>
        GetReconciliationMismatchCountsAsync(
            int storeId,
            DateTime sinceUtc,
            DateTime nowUtc,
            CancellationToken ct = default)
    {
        const decimal tolerance = 0.01m;
        var orders = _db.Orders
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.CompletedAtUtc.HasValue &&
                x.CompletedAtUtc.Value >= sinceUtc &&
                x.CompletedAtUtc.Value <= nowUtc &&
                x.LegalEntityAllocations.Any(a => !a.IsDeleted))
            .Select(x => new { x.Id, x.GrandTotal });

        var allocations = _db.OrderLegalEntityAllocations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .GroupBy(x => x.OrderId)
            .Select(x => new
            {
                OrderId = x.Key,
                Gross = x.Sum(y => y.NetAmount),
                Eligible = x.Sum(y => y.ProductVariant.HasInputInvoice ? y.NetAmount : 0m)
            });

        var allocationMismatchCount = await (
            from order in orders
            join allocation in allocations on order.Id equals allocation.OrderId
            where Math.Abs(allocation.Gross - order.GrandTotal) > tolerance
            select order.Id).CountAsync(ct);

        var invoiceMismatchCount = await (
            from order in orders
            join allocation in allocations on order.Id equals allocation.OrderId
            let reversedEligible = _db.OrderLegalEntityAllocationReversals
                .Where(x =>
                    x.StoreId == storeId &&
                    x.OrderId == order.Id &&
                    !x.IsDeleted &&
                    x.OrderLegalEntityAllocation.ProductVariant.HasInputInvoice)
                .Sum(x => (decimal?)x.FinancialAmount) ?? 0m
            let actual = _db.InvoiceHeads
                .Where(x =>
                    x.StoreId == storeId &&
                    x.OrderId == order.Id &&
                    !x.IsDeleted &&
                    x.OriginalInvoiceHeadId == null)
                .Sum(x => (decimal?)x.GrandTotal) ?? 0m
            let expected = allocation.Eligible - reversedEligible
            where Math.Abs(actual - expected) > tolerance
            select order.Id).CountAsync(ct);

        return (allocationMismatchCount, invoiceMismatchCount);
    }

    public Task AddEventAsync(
        LegalEntityActivationEvent activationEvent,
        CancellationToken ct = default)
        => _db.LegalEntityActivationEvents.AddAsync(activationEvent, ct).AsTask();

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}

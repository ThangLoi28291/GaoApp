using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceIntegrationLogRepository : IInvoiceIntegrationLogRepository
{
    private readonly AppDbContext _db;

    public InvoiceIntegrationLogRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(
        InvoiceIntegrationLog log,
        CancellationToken ct = default)
    {
        await _db.InvoiceIntegrationLogs.AddAsync(log, ct);
    }

    public async Task<List<InvoiceIntegrationLog>> GetLatestByInvoiceHeadAsync(
        int invoiceHeadId,
        int take = 20,
        CancellationToken ct = default)
    {
        if (take <= 0)
            take = 20;

        return await _db.InvoiceIntegrationLogs
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.InvoiceHeadId == invoiceHeadId)
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<(List<InvoiceIntegrationLog> Items, int Total)> QueryAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var q = _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .AsQueryable();

        if (query.FromDate.HasValue)
        {
            var fromUtc = query.FromDate.Value.Date;
            q = q.Where(x => x.StartedAtUtc >= fromUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusiveUtc = query.ToDate.Value.Date.AddDays(1);
            q = q.Where(x => x.StartedAtUtc < toExclusiveUtc);
        }

        if (query.InvoiceHeadId.HasValue && query.InvoiceHeadId.Value > 0)
        {
            q = q.Where(x => x.InvoiceHeadId == query.InvoiceHeadId.Value);
        }

        if (query.OrderId.HasValue && query.OrderId.Value > 0)
        {
            q = q.Where(x =>
                x.InvoiceHead != null &&
                x.InvoiceHead.OrderId == query.OrderId.Value);
        }

        if (query.ActionType.HasValue)
        {
            q = q.Where(x => x.ActionType == query.ActionType.Value);
        }

        if (query.IsSuccess.HasValue)
        {
            q = q.Where(x => x.IsSuccess == query.IsSuccess.Value);
        }

        var keyword = query.Keyword?.Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(x =>
                (x.RequestUrl != null && x.RequestUrl.Contains(keyword)) ||
                (x.ErrorCode != null && x.ErrorCode.Contains(keyword)) ||
                (x.ErrorMessage != null && x.ErrorMessage.Contains(keyword)) ||
                (x.InvoiceHead != null &&
                    (
                        (x.InvoiceHead.InvoiceNumber != null && x.InvoiceHead.InvoiceNumber.Contains(keyword)) ||
                        (x.InvoiceHead.ProviderInvoiceNo != null && x.InvoiceHead.ProviderInvoiceNo.Contains(keyword)) ||
                        (x.InvoiceHead.TransactionUuid != null && x.InvoiceHead.TransactionUuid.Contains(keyword))
                    )
                ));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<InvoiceIntegrationLog?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        return _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}
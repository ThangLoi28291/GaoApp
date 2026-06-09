using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _db;

    public InvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Order?> GetOrderForInvoiceAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);
    }

    public Task<Order?> GetOrderWithLinesForInvoiceAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.Orders
            .Include(x => x.Lines)
                .ThenInclude(x => x.Variant)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadByOrderIdAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadWithDetailsByOrderIdAsync(
       int orderId,
       CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.Details)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }

    public async Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
        CancellationToken ct = default)
    {
        await _db.InvoiceHeads.AddAsync(invoiceHead, ct);
    }

    public async Task AddInvoiceDetailsAsync(
        List<InvoiceDetail> details,
        CancellationToken ct = default)
    {
        await _db.InvoiceDetails.AddRangeAsync(details, ct);
    }
    public Task<InvoiceHead?> GetInvoiceHeadWithDetailsByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }

    public async Task AddInvoiceDetailAsync(
        InvoiceDetail detail,
        CancellationToken ct = default)
    {
        await _db.InvoiceDetails.AddAsync(detail, ct);
    }
    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<(List<InvoiceHead> Items, int Total)> QueryInvoiceHeadsAsync(
    DateTime? fromDate,
    DateTime? toDate,
    int? orderId,
    string? keyword,
    int page,
    int pageSize,
    CancellationToken ct = default)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var query = _db.InvoiceHeads
            .Include(x => x.Order)
            .Include(x => x.Details)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(x => x.InvoiceDate >= from);
        }

        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.InvoiceDate < toExclusive);
        }

        if (orderId.HasValue && orderId.Value > 0)
        {
            query = query.Where(x => x.OrderId == orderId.Value);
        }

        keyword = keyword?.Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                (x.InvoiceNumber != null && x.InvoiceNumber.Contains(keyword)) ||
                (x.BuyerName != null && x.BuyerName.Contains(keyword)) ||
                (x.Order != null && x.Order.OrderNumber != null && x.Order.OrderNumber.Contains(keyword)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<InvoiceHead?> GetInvoiceHeadDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.Order)
            .Include(x => x.Details)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }
    public Task<InvoiceDetail?> GetInvoiceDetailByIdAsync(
    int invoiceDetailId,
    CancellationToken ct = default)
    {
        return _db.InvoiceDetails
            .FirstOrDefaultAsync(x => x.Id == invoiceDetailId, ct);
    }

    public void UpdateInvoiceHead(InvoiceHead invoiceHead)
    {
        _db.InvoiceHeads.Update(invoiceHead);
    }
}
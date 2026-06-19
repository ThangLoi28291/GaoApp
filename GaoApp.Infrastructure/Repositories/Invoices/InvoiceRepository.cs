using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
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
        InvoiceListDisplayMode displayMode,
        InvoiceListSortMode sortMode,
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
                (x.Order != null &&
                 x.Order.OrderNumber != null &&
                 x.Order.OrderNumber.Contains(keyword)));
        }

        // =========================================================
        // FILTER HIỂN THỊ
        // =========================================================
        switch (displayMode)
        {
            case InvoiceListDisplayMode.HasQuantity:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0));
                break;

            case InvoiceListDisplayMode.Empty:
                query = query.Where(x =>
                    !x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0));
                break;

            case InvoiceListDisplayMode.Locked:
                query = query.Where(x => x.IsLocked);
                break;

            case InvoiceListDisplayMode.Unlocked:
                query = query.Where(x => !x.IsLocked);
                break;

            case InvoiceListDisplayMode.HasAutoLines:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.SourceType == InvoiceDetailSourceType.FromOrderLine));
                break;

            case InvoiceListDisplayMode.HasManualLines:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.SourceType == InvoiceDetailSourceType.Manual));
                break;

            case InvoiceListDisplayMode.All:
            default:
                break;
        }

        var total = await query.CountAsync(ct);

        // =========================================================
        // SORT DANH SÁCH
        // =========================================================
        // GHI CHÚ:
        // Phần này cho phép người dùng chọn nhiều kiểu sắp xếp.
        // Mặc định là HasLinesThenOrderIdDesc:
        // - Có sản phẩm lên trước.
        // - Sau đó OrderId mới nhất lên trước.
        IOrderedQueryable<InvoiceHead> orderedQuery = sortMode switch
        {
            InvoiceListSortMode.HasLinesThenOrderIdDesc => query
                .OrderByDescending(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.OrderIdDesc => query
                .OrderByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.OrderIdAsc => query
                .OrderBy(x => x.OrderId)
                .ThenBy(x => x.Id),

            InvoiceListSortMode.InvoiceDateDesc => query
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.InvoiceDateAsc => query
                .OrderBy(x => x.InvoiceDate)
                .ThenBy(x => x.Id),

            InvoiceListSortMode.GrandTotalDesc => query
                .OrderByDescending(x => x.GrandTotal)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.GrandTotalAsc => query
                .OrderBy(x => x.GrandTotal)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.QuantityDesc => query
                .OrderByDescending(x => x.TotalQuantity)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.QuantityAsc => query
                .OrderBy(x => x.TotalQuantity)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.AutoLineCountDesc => query
                .OrderByDescending(x => x.Details.Count(d =>
                    !d.IsDeleted &&
                    d.SourceType == InvoiceDetailSourceType.FromOrderLine))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.ManualLineCountDesc => query
                .OrderByDescending(x => x.Details.Count(d =>
                    !d.IsDeleted &&
                    d.SourceType == InvoiceDetailSourceType.Manual))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            // GHI CHÚ:
            // Mặc định cuối cùng: OrderId mới nhất.
            _ => query
                .OrderByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id)
        };

        var items = await orderedQuery
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
    public async Task<List<InvoiceHead>> GetByTransactionUuidsAsync(
        List<string> transactionUuids,
        CancellationToken ct = default)
    {
        transactionUuids = transactionUuids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!transactionUuids.Any())
            return new List<InvoiceHead>();

        return await _db.InvoiceHeads
            .Where(x =>
                !x.IsDeleted &&
                x.TransactionUuid != null &&
                transactionUuids.Contains(x.TransactionUuid))
            .ToListAsync(ct);
    }

    public async Task<List<InvoiceHead>> GetByProviderInvoiceNosAsync(
        List<string> providerInvoiceNos,
        CancellationToken ct = default)
    {
        providerInvoiceNos = providerInvoiceNos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!providerInvoiceNos.Any())
            return new List<InvoiceHead>();

        return await _db.InvoiceHeads
            .Where(x =>
                !x.IsDeleted &&
                (
                    x.ProviderInvoiceNo != null && providerInvoiceNos.Contains(x.ProviderInvoiceNo) ||
                    x.InvoiceNumber != null && providerInvoiceNos.Contains(x.InvoiceNumber)
                ))
            .ToListAsync(ct);
    }

    public async Task<List<InvoiceHead>> GetInvoiceHeadsForDashboardAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        var from = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);

        return await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.InvoiceDate >= from &&
                x.InvoiceDate < toExclusive)
            .ToListAsync(ct);
    }
}
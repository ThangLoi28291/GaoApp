using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed class InvoiceBuyerSelfServiceRepository
    : IInvoiceBuyerSelfServiceRepository
{
    private readonly AppDbContext _db;

    public InvoiceBuyerSelfServiceRepository(
        AppDbContext db)
    {
        _db = db;
    }

    public Task<InvoiceBuyerSelfServiceRequest?>
        GetByTokenHashAsync(
            byte[] tokenHash,
            CancellationToken ct = default)
    {
        // Token là bearer credential toàn cục.
        // Không dựa vào StoreId do client gửi lên.
        // Bypass tenant filter, nhưng vẫn enforce soft-delete rõ ràng.
        return _db.InvoiceBuyerSelfServiceRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Order)
            .FirstOrDefaultAsync(
                x =>
                    !x.IsDeleted &&
                    x.Order != null &&
                    !x.Order.IsDeleted &&
                    x.TokenHash == tokenHash,
                ct);
    }

    public async Task AddAsync(
        InvoiceBuyerSelfServiceRequest request,
        CancellationToken ct = default)
    {
        await _db.InvoiceBuyerSelfServiceRequests
            .AddAsync(request, ct);
    }

    public async Task LockStoreAsync(int storeId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Invoice link access requires a transaction.");

        // Same lock as LockStoreForIssueAsync: check state only after acquiring it.
        _ = await _db.Stores.FromSqlInterpolated($"""
            SELECT * FROM [Stores] WITH (UPDLOCK,HOLDLOCK,ROWLOCK)
            WHERE [Id] = {storeId} AND [IsDeleted] = 0
            """).AsNoTracking().SingleOrDefaultAsync(ct);
    }

    public Task<List<InvoiceHead>> GetHeadsForAccessAsync(int storeId, int orderId, CancellationToken ct = default)
        => _db.InvoiceHeads.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.OrderId == orderId && x.OriginalInvoiceHeadId == null)
            .ToListAsync(ct);

    public async Task RevokeForOrderAsync(int storeId, int orderId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await _db.InvoiceBuyerSelfServiceRequests
            .Where(x => x.StoreId == storeId && x.OrderId == orderId && x.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var row in rows)
            row.RevokedAtUtc = nowUtc;
    }

    public async Task MarkSubmittedAsync(int storeId, int requestId, DateTime nowUtc, CancellationToken ct = default)
    {
        var row = await _db.InvoiceBuyerSelfServiceRequests
            .SingleAsync(x => x.StoreId == storeId && x.Id == requestId && x.RevokedAtUtc == null, ct);
        row.LastSubmittedAtUtc = nowUtc;
    }

    public Task SaveChangesAsync(
        CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

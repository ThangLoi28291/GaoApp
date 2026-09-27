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

    public Task SaveChangesAsync(
        CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
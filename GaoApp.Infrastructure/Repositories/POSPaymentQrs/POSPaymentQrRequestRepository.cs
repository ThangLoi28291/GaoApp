using GaoApp.Application.Interfaces.Repositories.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.POSPaymentQrs;

public class POSPaymentQrRequestRepository : IPOSPaymentQrRequestRepository
{
    private readonly AppDbContext _db;

    public POSPaymentQrRequestRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PosPaymentQrRequest entity, CancellationToken ct = default)
    {
        await _db.PosPaymentQrRequests.AddAsync(entity, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public Task<PosPaymentQrRequest?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return _db.PosPaymentQrRequests
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }
    public Task<PosPaymentQrRequest?> GetLatestByContentAsync(
     string content,
     CancellationToken ct = default)
    {
        var normalized = (content ?? string.Empty).Trim();

        return _db.PosPaymentQrRequests
            .Where(x => x.Content == normalized)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }
}
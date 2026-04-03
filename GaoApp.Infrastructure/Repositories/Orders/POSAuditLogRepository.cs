using GaoApp.Application.Interfaces.Repositories.Orders;

using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;

namespace GaoApp.Infrastructure.Repositories.Orders;

public class POSAuditLogRepository : IPOSAuditLogRepository
{
    private readonly AppDbContext _db;

    public POSAuditLogRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(POSAuditLog log, CancellationToken ct = default)
    {
        await _db.POSAuditLogs.AddAsync(log, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}
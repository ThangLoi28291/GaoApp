using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IPOSAuditLogRepository
{
    Task AddAsync(POSAuditLog log, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
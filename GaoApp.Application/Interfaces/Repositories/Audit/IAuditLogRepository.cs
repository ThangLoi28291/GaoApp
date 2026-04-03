using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Audit;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken ct = default);
    Task<AuditLog?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<AuditLogListItemDto>> SearchAsync(int? storeId, AuditLogQueryDto query, CancellationToken ct = default);
}
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;

namespace GaoApp.Application.Interfaces.Services.Audit;

public interface IAuditLogService
{
    Task WriteAsync(WriteAuditLogRequest request, CancellationToken ct = default);
    Task<PagedResult<AuditLogListItemDto>> SearchAsync(AuditLogQueryDto query, CancellationToken ct = default);
    Task<AuditLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default);
}
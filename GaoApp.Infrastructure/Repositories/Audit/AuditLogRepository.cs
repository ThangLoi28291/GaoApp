using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Repositories.Audit;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Audit;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _context;

    public AuditLogRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken ct = default)
    {
        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken ct = default)
    {
        _context.AuditLogs.AddRange(auditLogs);
        await _context.SaveChangesAsync(ct);
    }

    public Task<AuditLog?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return _context.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PagedResult<AuditLogListItemDto>> SearchAsync(int? storeId, AuditLogQueryDto query, CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var q = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (storeId.HasValue)
            q = q.Where(x => x.StoreId == storeId.Value);

        if (query.FromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc <= query.ToUtc.Value);

        if (query.ActorUserId.HasValue)
            q = q.Where(x => x.ActorUserId == query.ActorUserId.Value);

        if (query.Module.HasValue)
            q = q.Where(x => (int)x.Module == query.Module.Value);

        if (query.ActionType.HasValue)
            q = q.Where(x => (int)x.ActionType == query.ActionType.Value);

        if (!string.IsNullOrWhiteSpace(query.EntityName))
            q = q.Where(x => x.EntityName == query.EntityName);

        if (!string.IsNullOrWhiteSpace(query.EntityId))
            q = q.Where(x => x.EntityId == query.EntityId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            q = q.Where(x =>
                (x.ActorUserName != null && x.ActorUserName.Contains(keyword)) ||
                (x.EntityName != null && x.EntityName.Contains(keyword)) ||
                (x.EntityDisplay != null && x.EntityDisplay.Contains(keyword)) ||
                (x.Summary != null && x.Summary.Contains(keyword)) ||
                (x.Path != null && x.Path.Contains(keyword)));
        }

        var totalItems = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogListItemDto
            {
                Id = x.Id,
                CreatedAtUtc = x.CreatedAtUtc,
                StoreId = x.StoreId,
                ActorUserId = x.ActorUserId,
                ActorUserName = x.ActorUserName,
                Module = x.Module.ToString(),
                ActionType = x.ActionType.ToString(),
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                EntityDisplay = x.EntityDisplay,
                Summary = x.Summary,
                IsSuccess = x.IsSuccess
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogListItemDto>(page, pageSize, totalItems, items);
    }
}
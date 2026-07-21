using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Repositories.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Audit;

/// <summary>
/// Service ghi AuditLog chung toàn hệ thống.
/// 
/// Nguồn dữ liệu:
/// - request truyền vào
/// - nếu request không truyền thì fallback sang execution context hiện tại
///   (user, store, path, ip, trace...)
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditExecutionContextAccessor _auditExecutionContextAccessor;

    public AuditLogService(
        IAuditLogRepository auditLogRepository,
        IAuditExecutionContextAccessor auditExecutionContextAccessor)
    {
        _auditLogRepository = auditLogRepository;
        _auditExecutionContextAccessor = auditExecutionContextAccessor;
    }

    public async Task WriteAsync(WriteAuditLogRequest request, CancellationToken ct = default)
    {
        var ctx = _auditExecutionContextAccessor.GetCurrent();

        // Lấy StoreId từ request hoặc execution context
        var storeId = request.StoreId ?? ctx.StoreId;

        if (!storeId.HasValue)
        {
            throw new InvalidOperationException(
                "AuditLog yêu cầu StoreId nhưng context hiện tại không có.");
        }

        var entity = new AuditLog
        {
            StoreId = storeId.Value,

            ActorUserId = request.ActorUserId ?? ctx.UserId,
            ActorUserName = request.ActorUserName ?? ctx.UserName,

            Module = request.Module,
            ActionType = request.ActionType,

            EntityName = request.EntityName,
            EntityId = request.EntityId,
            EntityDisplay = request.EntityDisplay,
            Summary = request.Summary,

            OldValuesJson = request.OldValuesJson,
            NewValuesJson = request.NewValuesJson,
            ChangedColumnsJson = request.ChangedColumnsJson,

            TraceId = request.TraceId ?? ctx.TraceId,
            IpAddress = request.IpAddress ?? ctx.IpAddress,
            UserAgent = request.UserAgent ?? ctx.UserAgent,
            Path = request.Path ?? ctx.Path,

            IsSuccess = request.IsSuccess,
            ErrorMessage = request.ErrorMessage,
            CreatedAtUtc = request.CreatedAtUtc ?? DateTime.UtcNow
        };

        await _auditLogRepository.AddAsync(entity, ct);
    }

    public Task<PagedResult<AuditLogListItemDto>> SearchAsync(AuditLogQueryDto query, CancellationToken ct = default)
    {
        var ctx = _auditExecutionContextAccessor.GetCurrent();
        return _auditLogRepository.SearchAsync(ctx.StoreId, query, ct);
    }

    public async Task<AuditLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
    {
        var entity = await _auditLogRepository.GetByIdAsync(id, ct);
        if (entity == null)
            return null;

        return new AuditLogDetailDto
        {
            Id = entity.Id,
            CreatedAtUtc = entity.CreatedAtUtc,
            StoreId = entity.StoreId,
            ActorUserId = entity.ActorUserId,
            ActorUserName = entity.ActorUserName,
            Module = entity.Module.ToString(),
            ActionType = entity.ActionType.ToString(),
            EntityName = entity.EntityName,
            EntityId = entity.EntityId,
            EntityDisplay = entity.EntityDisplay,
            Summary = entity.Summary,
            OldValuesJson = entity.OldValuesJson,
            NewValuesJson = entity.NewValuesJson,
            ChangedColumnsJson = entity.ChangedColumnsJson,
            TraceId = entity.TraceId,
            IpAddress = entity.IpAddress,
            UserAgent = entity.UserAgent,
            Path = entity.Path,
            IsSuccess = entity.IsSuccess,
            ErrorMessage = entity.ErrorMessage
        };
    }
}
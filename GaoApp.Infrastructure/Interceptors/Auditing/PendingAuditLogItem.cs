using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GaoApp.Infrastructure.Interceptors.Auditing;

/// <summary>
/// Snapshot tạm 1 audit item trước khi EF SaveChanges.
/// Giữ nguyên dữ liệu old/new/change trước khi EF reset state.
/// </summary>
public class PendingAuditLogItem
{
    public int StoreId { get; set; }
    public int? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }

    public AuditModuleType Module { get; set; }
    public AuditActionType ActionType { get; set; }

    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? EntityDisplay { get; set; }
    public string? Summary { get; set; }
    public EntityEntry? Entry { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? ChangedColumnsJson { get; set; }

    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Path { get; set; }

    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
namespace GaoApp.Application.DTOs.Audit;

/// <summary>
/// Dữ liệu chi tiết audit log.
/// </summary>
public class AuditLogDetailDto
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public int? StoreId { get; set; }
    public int? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }

    public string? Module { get; set; }
    public string? ActionType { get; set; }

    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? EntityDisplay { get; set; }
    public string? Summary { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? ChangedColumnsJson { get; set; }

    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Path { get; set; }

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
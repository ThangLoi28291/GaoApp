namespace GaoApp.Application.DTOs.Audit;

/// <summary>
/// Dữ liệu dòng hiển thị ở grid audit logs.
/// </summary>
public class AuditLogListItemDto
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

    public bool IsSuccess { get; set; }
}
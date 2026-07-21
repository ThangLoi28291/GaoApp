namespace GaoApp.Application.DTOs.Audit;

/// <summary>
/// Bộ lọc tra cứu audit log.
/// </summary>
public class AuditLogQueryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public int? ActorUserId { get; set; }
    public int? Module { get; set; }
    public int? ActionType { get; set; }

    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
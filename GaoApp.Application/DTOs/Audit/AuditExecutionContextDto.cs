namespace GaoApp.Application.DTOs.Audit;

/// <summary>
/// Context runtime của request hiện tại để gắn vào audit log.
/// </summary>
public class AuditExecutionContextDto
{
    public int? StoreId { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Path { get; set; }
}
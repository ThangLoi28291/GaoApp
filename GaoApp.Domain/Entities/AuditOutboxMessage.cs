namespace GaoApp.Domain.Entities;

/// <summary>Durable audit evidence. Payload is immutable; only the consumer updates delivery metadata.</summary>
public sealed class AuditOutboxMessage
{
    public Guid EventId { get; set; }
    public int StoreId { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorType { get; set; }
}

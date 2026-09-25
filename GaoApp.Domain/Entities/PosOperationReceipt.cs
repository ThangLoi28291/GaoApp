using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Durable response for one terminal operation. Never recycle keys after a sale is removed.</summary>
public sealed class PosOperationReceipt : BaseStoreEntity
{
    public Guid OperationId { get; set; }
    public int TerminalId { get; set; }
    public int UserId { get; set; }
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public bool WasOffline { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

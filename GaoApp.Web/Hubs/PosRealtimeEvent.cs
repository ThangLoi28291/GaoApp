namespace GaoApp.Web.Hubs;

public sealed class PosRealtimeEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = "";
    public int StoreId { get; set; }
    public string TerminalId { get; set; } = "";
    public int? OrderId { get; set; }
    public int? RelatedOrderId { get; set; }
    public bool CartChanged { get; set; }
    public bool HeldChanged { get; set; }
    public bool SummaryChanged { get; set; }
    public bool PaymentsChanged { get; set; }
    public bool CustomerChanged { get; set; }
    public string Message { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
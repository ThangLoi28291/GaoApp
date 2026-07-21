using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Hubs;

public sealed class PosRealtimeNotifier : IPosRealtimeNotifier
{
    private readonly IHubContext<PosHub> _hubContext;

    public PosRealtimeNotifier(IHubContext<PosHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyStoreAsync(
        int storeId,
        string eventType,
        string terminalId,
        int? orderId = null,
        int? relatedOrderId = null,
        bool cartChanged = false,
        bool heldChanged = false,
        bool summaryChanged = false,
        bool paymentsChanged = false,
        bool customerChanged = false,
        string? message = null,
        CancellationToken ct = default)
    {
        if (storeId <= 0) return;

        var payload = BuildPayload(
            storeId, terminalId, eventType, orderId, relatedOrderId,
            cartChanged, heldChanged, summaryChanged, paymentsChanged, customerChanged, message);

        await _hubContext.Clients
            .Group($"store:{storeId}")
            .SendAsync("pos:event", payload, ct);
    }

    public async Task NotifyTerminalAsync(
        int storeId,
        string terminalId,
        string eventType,
        int? orderId = null,
        int? relatedOrderId = null,
        bool cartChanged = false,
        bool heldChanged = false,
        bool summaryChanged = false,
        bool paymentsChanged = false,
        bool customerChanged = false,
        string? message = null,
        CancellationToken ct = default)
    {
        terminalId = (terminalId ?? string.Empty).Trim();

        if (storeId <= 0 || string.IsNullOrWhiteSpace(terminalId)) return;

        var payload = BuildPayload(
            storeId, terminalId, eventType, orderId, relatedOrderId,
            cartChanged, heldChanged, summaryChanged, paymentsChanged, customerChanged, message);

        await _hubContext.Clients
            .Group($"store:{storeId}:terminal:{terminalId}")
            .SendAsync("pos:event", payload, ct);
    }

    private static PosRealtimeEvent BuildPayload(
        int storeId,
        string terminalId,
        string eventType,
        int? orderId,
        int? relatedOrderId,
        bool cartChanged,
        bool heldChanged,
        bool summaryChanged,
        bool paymentsChanged,
        bool customerChanged,
        string? message)
    {
        return new PosRealtimeEvent
        {
            StoreId = storeId,
            TerminalId = terminalId?.Trim() ?? string.Empty,
            EventType = eventType?.Trim() ?? string.Empty,
            OrderId = orderId,
            RelatedOrderId = relatedOrderId,
            CartChanged = cartChanged,
            HeldChanged = heldChanged,
            SummaryChanged = summaryChanged,
            PaymentsChanged = paymentsChanged,
            CustomerChanged = customerChanged,
            Message = message?.Trim() ?? string.Empty
        };
    }
}
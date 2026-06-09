public interface IPosRealtimeNotifier
{
    Task NotifyStoreAsync(
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
        CancellationToken ct = default);

    Task NotifyTerminalAsync(
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
        CancellationToken ct = default);
}
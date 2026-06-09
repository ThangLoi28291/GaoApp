using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Hubs;

public class PosHub : Hub
{
    public async Task JoinStoreGroup(int storeId, string terminalId)
    {
        if (storeId <= 0)
            throw new HubException("Invalid store.");

        terminalId = (terminalId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(terminalId))
            throw new HubException("Invalid terminal.");

        var storeGroupName = $"store:{storeId}";
        var terminalGroupName = $"store:{storeId}:terminal:{terminalId}";

        await Groups.AddToGroupAsync(Context.ConnectionId, storeGroupName);
        await Groups.AddToGroupAsync(Context.ConnectionId, terminalGroupName);

        Context.Items["storeId"] = storeId;
        Context.Items["terminalId"] = terminalId;

        await Clients.Caller.SendAsync("joined", new
        {
            storeId,
            terminalId,
            storeGroupName,
            terminalGroupName
        });
    }
    public sealed class PosTerminalClientEvent
    {
        public string EventType { get; set; } = "";
        public object? Payload { get; set; }
    }
    public async Task BroadcastTerminalEvent(PosTerminalClientEvent request)
    {
        var storeId = Context.Items.ContainsKey("storeId")
            ? Context.Items["storeId"] as int?
            : null;

        var terminalId = Context.Items.ContainsKey("terminalId")
            ? Context.Items["terminalId"]?.ToString()
            : null;

        if (!storeId.HasValue || storeId.Value <= 0 || string.IsNullOrWhiteSpace(terminalId))
            throw new HubException("Connection has not joined terminal group.");

        var terminalGroupName = $"store:{storeId.Value}:terminal:{terminalId}";

        await Clients.Group(terminalGroupName).SendAsync("pos:event", new
        {
            eventType = request.EventType,
            terminalId,
            storeId = storeId.Value,
            payload = request.Payload,
            occurredAtUtc = DateTime.UtcNow
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var storeId = Context.Items.ContainsKey("storeId")
            ? Context.Items["storeId"] as int?
            : null;

        var terminalId = Context.Items.ContainsKey("terminalId")
            ? Context.Items["terminalId"]?.ToString()
            : null;

        if (storeId.HasValue && storeId.Value > 0)
        {
            var storeGroupName = $"store:{storeId.Value}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, storeGroupName);

            if (!string.IsNullOrWhiteSpace(terminalId))
            {
                var terminalGroupName = $"store:{storeId.Value}:terminal:{terminalId}";
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, terminalGroupName);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
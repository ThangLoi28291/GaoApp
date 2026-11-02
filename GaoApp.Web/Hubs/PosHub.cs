using System.Globalization;
using GaoApp.Application.Common.Security;
using System.Text.Json;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Hubs;

[Authorize(Policy = PermissionCodes.Pos.Order.View)]
public class PosHub(CustomerPaymentDisplayService display) : Hub
{
    public async Task JoinStoreGroup(int storeId, string terminalId)
    {
        var storeIdClaim = Context.User?.FindFirst("store_id")?.Value;

        if (!int.TryParse(
                storeIdClaim,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var authenticatedStoreId) ||
            authenticatedStoreId <= 0 ||
            storeId <= 0 ||
            storeId != authenticatedStoreId)
        {
            throw new HubException("Store không thuộc phiên hiện tại.");
        }

        var terminalIdClaim = Context.User?.FindFirst("terminal_id")?.Value;

        if (!int.TryParse(
                terminalIdClaim,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var authenticatedTerminalId) ||
            authenticatedTerminalId <= 0 ||
            !int.TryParse(
                (terminalId ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var requestedTerminalId) ||
            requestedTerminalId <= 0 ||
            requestedTerminalId != authenticatedTerminalId)
        {
            throw new HubException("Terminal không thuộc phiên hiện tại.");
        }

        terminalId = requestedTerminalId.ToString(CultureInfo.InvariantCulture);

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
        public JsonElement Payload { get; set; }
    }
    // PosSessionHubFilter checks current pos.payment.create for this invocation before entering the method.
    public async Task BroadcastTerminalEvent(PosTerminalClientEvent request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EventType))
            throw new HubException("Event không hợp lệ.");
        if (request.EventType is not ("customer_payment_preview" or "customer_payment_hide" or
            "customer_payment_qr_created" or "customer_payment_refresh"))
            throw new HubException("Event này chỉ được phát từ máy chủ.");

        var storeId = Context.Items.ContainsKey("storeId")
            ? Context.Items["storeId"] as int?
            : null;

        var terminalId = Context.Items.ContainsKey("terminalId")
            ? Context.Items["terminalId"]?.ToString()
            : null;

        if (!storeId.HasValue || storeId.Value <= 0 || string.IsNullOrWhiteSpace(terminalId))
            throw new HubException("Connection has not joined terminal group.");

        if (request.EventType != "customer_payment_refresh")
        {
            if (request.Payload.ValueKind != JsonValueKind.Object) throw new HubException("Thông tin hiển thị không hợp lệ.");
            CustomerPaymentDisplayCommand command;
            try
            {
                command = request.Payload.Deserialize<CustomerPaymentDisplayCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new JsonException();
            }
            catch (JsonException) { throw new HubException("Thông tin hiển thị không hợp lệ."); }
            try
            {
                await display.ApplyAsync(storeId.Value, int.Parse(terminalId, CultureInfo.InvariantCulture), request.EventType,
                    command, Context.ConnectionAborted);
            }
            catch (GaoApp.Application.Common.Exceptions.ValidationAppException ex) { throw new HubException(ex.SafeMessage); }
        }

        var terminalGroupName = $"store:{storeId.Value}:terminal:{terminalId}";

        await Clients.Group(terminalGroupName).SendAsync("pos:event", new
        {
            eventType = "customer_payment_display_changed",
            terminalId,
            storeId = storeId.Value,
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

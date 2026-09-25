using System.Collections.Concurrent;
using System.Security.Claims;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Hubs;

/// <summary>Single-process transport; all outgoing paths revalidate their explicit recipient snapshot.</summary>
public sealed class RevocablePosHubLifetimeManager(
    PosRealtimeSessionValidator validator,
    ILogger<DefaultHubLifetimeManager<PosHub>> logger) : DefaultHubLifetimeManager<PosHub>(logger)
{
    private sealed record Connection(HubConnectionContext Context, PosRealtimeSessionValidator.Session Session,
        string PermissionStamp, bool CanBroadcastPayment)
    {
        public ConcurrentDictionary<string, byte> Groups { get; } = new(StringComparer.Ordinal);
    }
    private readonly ConcurrentDictionary<string, Connection> connections = new(StringComparer.Ordinal);

    public override async Task OnConnectedAsync(HubConnectionContext connection)
    {
        var principal = connection.User;
        if (principal.Identity?.IsAuthenticated != true ||
            !PositiveClaim(principal, ClaimTypes.NameIdentifier, out var user) ||
            !PositiveClaim(principal, "store_id", out var store) ||
            !PositiveClaim(principal, "terminal_id", out var terminal))
        { connection.Abort(); throw new HubException("Phiên POS không hợp lệ."); }
        var session = new PosRealtimeSessionValidator.Session(connection.ConnectionId, principal, user, store, terminal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var valid = await validator.ValidateAsync([session], deadline.Token);
            if (!valid.TryGetValue(connection.ConnectionId, out var stamp))
                throw new HubException("Phiên POS không còn quyền truy cập. Vui lòng đăng nhập lại.");
            connections[connection.ConnectionId] = new(connection, session, stamp.PermissionStamp, stamp.CanBroadcastPayment);
            await base.OnConnectedAsync(connection);
        }
        catch { connections.TryRemove(connection.ConnectionId, out _); connection.Abort(); throw; }
    }

    public override Task OnDisconnectedAsync(HubConnectionContext connection)
    {
        connections.TryRemove(connection.ConnectionId, out _);
        return base.OnDisconnectedAsync(connection);
    }

    public async Task EnsureCurrentAsync(string connectionId, CancellationToken ct, bool requirePaymentPermission = false)
    {
        if (!connections.TryGetValue(connectionId, out var connection) ||
            (await CurrentRecipientsAsync([connection], ct)).Length == 0)
            throw new HubException("Phiên POS đã thay đổi hoặc hết quyền. Vui lòng đăng nhập lại.");
        if (requirePaymentPermission && !connection.CanBroadcastPayment)
            throw new HubException("Không có quyền phát thông báo thanh toán.");
    }

    public Task RevalidateConnectionsAsync(CancellationToken ct) => CurrentRecipientsAsync(connections.Values.ToArray(), ct);

    public void RevokeSession(ClaimsPrincipal principal)
    {
        // Close this signed-in user's connections for the same store/terminal and credential generation.
        foreach (var connection in connections.Values)
            if (new[] { ClaimTypes.NameIdentifier, "store_id", "terminal_id", AuthSessionStamp.ClaimType }
                .All(claim => principal.FindFirstValue(claim) is string value && value == connection.Session.Principal.FindFirstValue(claim)))
                Revoke(connection);
    }

    public void RevokeUser(int userId)
    {
        foreach (var connection in connections.Values)
            if (connection.Session.UserId == userId) Revoke(connection);
    }

    private async Task<string[]> CurrentRecipientsAsync(Connection[] candidates, CancellationToken ct)
    {
        if (candidates.Length == 0) return [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        Dictionary<string, PosRealtimeSessionValidator.Access> valid;
        try { valid = await validator.ValidateAsync(candidates.Select(x => x.Session).ToArray(), deadline.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // If current access cannot be established, no data is delivered to this recipient snapshot.
            foreach (var candidate in candidates) Revoke(candidate);
            throw;
        }
        var ids = new List<string>();
        foreach (var candidate in candidates)
        {
            if (valid.TryGetValue(candidate.Session.ConnectionId, out var stamp) && stamp.PermissionStamp == candidate.PermissionStamp &&
                !candidate.Context.ConnectionAborted.IsCancellationRequested &&
                connections.TryGetValue(candidate.Session.ConnectionId, out var current) && ReferenceEquals(candidate, current))
                ids.Add(candidate.Session.ConnectionId);
            else Revoke(candidate);
        }
        return ids.ToArray();
    }

    private void Revoke(Connection connection)
    {
        connections.TryRemove(connection.Session.ConnectionId, out _);
        connection.Context.Abort();
    }

    public override async Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(connectionId, cancellationToken);
        if (!connections.TryGetValue(connectionId, out var connection)) throw new HubException("Phiên POS đã đóng.");
        var storeGroup = $"store:{connection.Session.StoreId}";
        if (groupName != storeGroup && groupName != $"{storeGroup}:terminal:{connection.Session.TerminalId}")
            throw new HubException("Nhóm không thuộc cửa hàng/terminal của phiên.");
        await base.AddToGroupAsync(connectionId, groupName, cancellationToken);
        connection.Groups[groupName] = 0;
    }
    public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (connections.TryGetValue(connectionId, out var connection)) connection.Groups.TryRemove(groupName, out _);
        return base.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
    }

    private async Task SendAsync(Func<Connection, bool> matches, string method, object?[] args, CancellationToken ct)
    {
        var ids = await CurrentRecipientsAsync(connections.Values.Where(matches).ToArray(), ct);
        if (ids.Length > 0) await base.SendConnectionsAsync(ids, method, args, ct);
    }
    public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(_ => true, methodName, args, cancellationToken);
    public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        => SendAsync(c => !excludedConnectionIds.Contains(c.Session.ConnectionId), methodName, args, cancellationToken);
    public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => c.Session.ConnectionId == connectionId, methodName, args, cancellationToken);
    public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => connectionIds.Contains(c.Session.ConnectionId), methodName, args, cancellationToken);
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => c.Groups.ContainsKey(groupName), methodName, args, cancellationToken);
    public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => groupNames.Any(c.Groups.ContainsKey), methodName, args, cancellationToken);
    public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        => SendAsync(c => c.Groups.ContainsKey(groupName) && !excludedConnectionIds.Contains(c.Session.ConnectionId), methodName, args, cancellationToken);
    public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => c.Context.UserIdentifier == userId, methodName, args, cancellationToken);
    public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        => SendAsync(c => c.Context.UserIdentifier is string id && userIds.Contains(id), methodName, args, cancellationToken);
    public override async Task<T> InvokeConnectionAsync<T>(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken)
    {
        await EnsureCurrentAsync(connectionId, cancellationToken);
        return await base.InvokeConnectionAsync<T>(connectionId, methodName, args, cancellationToken);
    }
    private static bool PositiveClaim(ClaimsPrincipal principal, string claim, out int value)
        => int.TryParse(principal.FindFirstValue(claim), out value) && value > 0;
}

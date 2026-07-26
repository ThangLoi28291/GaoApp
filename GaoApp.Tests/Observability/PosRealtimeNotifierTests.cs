using GaoApp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class PosRealtimeNotifierTests
{
    [Fact]
    public async Task Notification_failure_is_best_effort_and_has_safe_telemetry()
    {
        const string secret = "synthetic-signalr-secret";
        var proxy = new ThrowingClientProxy(new InvalidOperationException(secret));
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<PosRealtimeNotifier>();
        var notifier = new PosRealtimeNotifier(
            new FakeHubContext(proxy),
            logger);

        await notifier.NotifyStoreAsync(
            storeId: 12,
            eventType: "order_finalized",
            terminalId: "terminal-1",
            orderId: 42);

        var entry = Assert.Single(
            logger.Entries,
            x => x.Level == LogLevel.Warning);
        Assert.Contains("order_finalized", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_logged_as_error()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var proxy = new ThrowingClientProxy(
            new OperationCanceledException(cts.Token));
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<PosRealtimeNotifier>();
        var notifier = new PosRealtimeNotifier(
            new FakeHubContext(proxy),
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => notifier.NotifyStoreAsync(
                storeId: 12,
                eventType: "cart_changed",
                terminalId: "terminal-1",
                ct: cts.Token));

        Assert.DoesNotContain(logger.Entries, x => x.Level == LogLevel.Error);
    }

    private sealed class FakeHubContext(IClientProxy proxy) : IHubContext<PosHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients(proxy);
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeHubClients(IClientProxy proxy) : IHubClients
    {
        public IClientProxy All => proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Client(string connectionId) => proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
        public IClientProxy Group(string groupName) => proxy;
        public IClientProxy GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
        public IClientProxy User(string userId) => proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingClientProxy(Exception exception) : IClientProxy
    {
        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
            => Task.FromException(exception);
    }
}

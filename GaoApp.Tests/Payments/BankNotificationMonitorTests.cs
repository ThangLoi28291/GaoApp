using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;

namespace GaoApp.Tests.Payments;

public sealed class BankNotificationMonitorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static BankNotificationDevice Phone(int store = 1, int id = 2) => new() { StoreId = store, Id = id, Name = "Test phone" };

    [Fact]
    public void Never_contacted_phone_is_unknown_but_persisted_contact_survives_monitor_restart()
    {
        var clock = new Clock(); var monitor = new BankNotificationMonitor(clock); var device = Phone();
        Assert.Equal("Unknown", monitor.State(device).State);
        device.LastSeenAtUtc = clock.Now.UtcDateTime;
        Assert.Equal("Connected", monitor.State(device).State);
        Assert.Null(monitor.State(device).QueuedCount);
        Assert.Null(monitor.State(device).LastErrorCode);
        clock.Now = clock.Now.AddSeconds(91);
        Assert.Equal("Disconnected", monitor.State(device).State);
    }

    [Fact]
    public void Contact_expires_after_existing_90_second_limit_and_does_not_guess_screen_or_network_state()
    {
        var clock = new Clock(); var monitor = new BankNotificationMonitor(clock); var device = Phone();
        monitor.Contact(device.StoreId, device.Id);
        Assert.Equal("Connected", monitor.State(device).State);
        Assert.Null(monitor.State(device).QueuedCount);
        Assert.Null(monitor.State(device).LastErrorCode);
        clock.Now = clock.Now.AddSeconds(90);
        Assert.Equal("Connected", monitor.State(device).State);
        clock.Now = clock.Now.AddMilliseconds(1);
        Assert.Equal("Disconnected", monitor.State(device).State);
        monitor.Contact(device.StoreId, device.Id);
        Assert.Equal("Connected", monitor.State(device).State);
    }

    [Theory]
    [InlineData(3, 0, 503, "Queued")]
    [InlineData(3, 2, 400, "SendError")]
    public void Upload_contact_preserves_last_queue_and_error_until_the_phone_reports_a_new_heartbeat(int queued, int blocked, int code, string state)
    {
        var clock = new Clock(); var monitor = new BankNotificationMonitor(clock); var device = Phone();
        monitor.Report(device.StoreId, device.Id, new(queued, blocked, code));
        clock.Now = clock.Now.AddSeconds(91);
        Assert.Equal("Disconnected", monitor.State(device).State);
        monitor.Contact(device.StoreId, device.Id);
        var contact = monitor.State(device);
        Assert.Equal(state, contact.State);
        Assert.Equal(queued, contact.QueuedCount);
        Assert.Equal(code, contact.LastErrorCode);
        Assert.Equal(clock.Now.UtcDateTime, contact.LastContactAtUtc);
        monitor.Report(device.StoreId, device.Id, new(0, 0, 0));
        Assert.Equal("Connected", monitor.State(device).State);
        Assert.Equal(0, monitor.State(device).QueuedCount);
    }

    [Fact]
    public void Other_store_or_device_contact_does_not_change_phone_state_and_revocation_always_wins()
    {
        var clock = new Clock(); var monitor = new BankNotificationMonitor(clock); var device = Phone();
        monitor.Contact(2, device.Id); monitor.Contact(device.StoreId, 3);
        Assert.Equal("Unknown", monitor.State(device).State);
        monitor.Contact(device.StoreId, device.Id);
        device.Enabled = false;
        Assert.Equal("Revoked", monitor.State(device).State);
        Assert.Null(monitor.State(device).QueuedCount);
    }
}

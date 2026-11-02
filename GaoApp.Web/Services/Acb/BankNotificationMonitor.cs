using System.Collections.Concurrent;

namespace GaoApp.Web.Services.Acb;

public sealed record BankNotificationHeartbeat(int QueuedCount, int BlockedCount, int LastErrorCode);
public sealed record BankNotificationPhoneState(int DeviceId, string Name, string State, int? QueuedCount, int? LastErrorCode, DateTime? LastContactAtUtc);
public sealed class BankNotificationMonitor(TimeProvider? time = null)
{
    private sealed record Sample(DateTime At, BankNotificationHeartbeat? Value);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<(int Store, int Device), Sample> samples = new();
    public void Report(int store, int device, BankNotificationHeartbeat value) => samples[(store, device)] = new(clock.GetUtcNow().UtcDateTime, value);
    // An authenticated upload proves contact, including a lost-ACK replay or an
    // unrecognized notification. Only the phone's heartbeat can update its queue.
    public void Contact(int store, int device)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        samples.AddOrUpdate((store, device), new Sample(now, null), (_, sample) => sample with { At = now > sample.At ? now : sample.At });
    }
    public BankNotificationPhoneState State(GaoApp.Domain.Entities.BankNotificationDevice device)
    {
        if (!device.Enabled) return new(device.Id, device.Name, "Revoked", null, null, device.LastSeenAtUtc);
        samples.TryGetValue((device.StoreId, device.Id), out var sample);
        var lastContact = sample?.At;
        if (device.LastSeenAtUtc.HasValue && (!lastContact.HasValue || device.LastSeenAtUtc > lastContact)) lastContact = device.LastSeenAtUtc;
        if (!lastContact.HasValue) return new(device.Id, device.Name, "Unknown", null, null, null);
        var state = clock.GetUtcNow().UtcDateTime - lastContact.Value > TimeSpan.FromSeconds(90) ? "Disconnected"
            : sample?.Value?.BlockedCount > 0 ? "SendError" : sample?.Value?.QueuedCount > 0 ? "Queued" : "Connected";
        return new(device.Id, device.Name, state, sample?.Value?.QueuedCount, sample?.Value?.LastErrorCode, lastContact);
    }
}

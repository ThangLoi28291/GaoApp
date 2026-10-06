using System.Diagnostics;
using System.Text.Json;
using GaoApp.Infrastructure.Services.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryD02")]
public sealed class DeliveryD02OutboxTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Events_are_store_revision_bound_and_do_not_include_recipient_or_lookup_token()
    {
        using var c = await fixture.CaseAsync(); await c.PostAsync(c.Change());
        await using var db = c.Context();
        var events = await db.DeliveryOutboxMessages.Where(x => x.DeliveryOrderId == c.Detail.Id).OrderBy(x => x.Revision).ToListAsync();
        Assert.Equal(2, events.Count);
        foreach (var message in events)
        {
            Assert.Equal(c.Account.Store.StoreId, message.StoreId);
            var payload = JsonDocument.Parse(message.PayloadJson).RootElement;
            Assert.Equal(message.EventId, payload.GetProperty("eventId").GetGuid());
            Assert.Equal(message.Revision, payload.GetProperty("revision").GetInt32());
            Assert.Equal(message.StoreId, payload.GetProperty("storeId").GetInt32());
            Assert.Equal(c.Detail.Id, payload.GetProperty("deliveryId").GetInt32());
            Assert.False(payload.TryGetProperty("recipientPhone", out _));
            Assert.False(payload.TryGetProperty("recipientAddress", out _));
            Assert.False(payload.TryGetProperty("lookupToken", out _));
            Assert.Equal(8, Convert.FromBase64String(payload.GetProperty("version").GetString()!).Length);
        }
        Assert.Equal(2, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
    }
    [Fact]
    public async Task Concurrent_duplicate_consumers_ack_once_and_new_consumer_can_replay()
    {
        using var c = await fixture.CaseAsync();
        var consumer = "D02-test-" + Guid.NewGuid().ToString("N");
        async Task<int> Process(string name)
        {
            await using var db = c.Context(); return await new DeliveryOutboxProcessor(db).ProcessPendingAsync(name, take: 100);
        }
        await Task.WhenAll(Process(consumer), Process(consumer));
        Assert.Equal(0, await Process(consumer));
        await using var check = c.Context(); var message = await check.DeliveryOutboxMessages.SingleAsync(x => x.DeliveryOrderId == c.Detail.Id);
        Assert.Equal(1, await check.DeliveryOutboxReceipts.CountAsync(x => x.EventId == message.EventId && x.Consumer == consumer));
        await Process(consumer + "-next");
        Assert.Equal(1, await check.DeliveryOutboxReceipts.CountAsync(x => x.EventId == message.EventId && x.Consumer == consumer + "-next"));
        Assert.Equal(1, await check.DeliveryOutboxMessages.CountAsync(x => x.EventId == message.EventId));
    }
    [Fact]
    public async Task Process_restart_reads_delivery_and_replays_pending_events_without_duplicate_ack()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change(); var result = await c.PostAsync(request);
        await using (var db = c.Context())
        {
            Assert.Equal(2, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
            Assert.False(await db.DeliveryOutboxReceipts.AnyAsync(r =>
                r.Consumer == DeliveryOutboxProcessor.FoundationConsumer && db.DeliveryOutboxMessages.Any(m => m.EventId == r.EventId && m.DeliveryOrderId == c.Detail.Id)));
        }
        try
        {
            await fixture.Web.RestartAsync(enableDeliveryOutbox: true);
            var detail = await c.Client.JsonAsync(HttpMethod.Get, "/admin/api/deliveries/" + c.Detail.Id);
            Assert.Equal(result.Version, detail.GetProperty("version").GetString());
            var replay = await c.PostAsync(request); Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
            var deadline = Stopwatch.StartNew();
            int count = 0;
            while (deadline.Elapsed < TimeSpan.FromSeconds(20))
            {
                await using var db = c.Context();
                count = await db.DeliveryOutboxReceipts.CountAsync(r => r.Consumer == DeliveryOutboxProcessor.FoundationConsumer &&
                    db.DeliveryOutboxMessages.Any(m => m.EventId == r.EventId && m.DeliveryOrderId == c.Detail.Id));
                if (count == 2) break;
                await Task.Delay(200);
            }
            Assert.Equal(2, count);
            await fixture.Web.RestartAsync(enableDeliveryOutbox: true);
            await using var final = c.Context();
            await new DeliveryOutboxProcessor(final).ProcessPendingAsync();
            Assert.Equal(2, await final.DeliveryOutboxReceipts.CountAsync(r => r.Consumer == DeliveryOutboxProcessor.FoundationConsumer &&
                final.DeliveryOutboxMessages.Any(m => m.EventId == r.EventId && m.DeliveryOrderId == c.Detail.Id)));
            Assert.Equal(2, (await c.Client.JsonAsync(HttpMethod.Get, "/admin/api/deliveries/" + c.Detail.Id + "/history")).GetArrayLength());
        }
        finally { await fixture.Web.RestartAsync(enableDeliveryOutbox: false); }
    }
}

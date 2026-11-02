using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_phone_HTTP_uses_device_key_without_browser_CSRF_and_keeps_other_POSTs_protected()
    {
        await using var f = await SqlNotificationFixture.Create();
        var monitor = f.Monitor; var bridge = f.Bridge; var device = f.Device;
        var token = new string('B', 64);
        device.TokenHash = BankNotificationParser.Hash(token);
        await f.Db.SaveChangesAsync();
        var client = f.Client;
        const string endpoint = "/api/bank-notifications";
        client.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{device.Id}:{token}");
        var body = f.Body();
        Assert.Equal("Unknown", monitor.State(device).State);
        using (var accepted = await client.PostAsJsonAsync(endpoint, body))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var ack = await accepted.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(ack.GetProperty("accepted").GetBoolean());
            Assert.True(ack.GetProperty("receiptId").GetInt32() > 0);
        }
        using (var duplicate = await client.PostAsJsonAsync(endpoint, body)) Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        await f.AssertAuthenticatedIntakeOnly(); await f.RefreshDevice();
        Assert.NotNull(device.LastSeenAtUtc);
        Assert.Equal("Connected", monitor.State(device).State);
        Assert.Null(monitor.State(device).QueuedCount); // Uploads cannot invent phone queue counts.
        Assert.True(Assert.Single(await bridge.PendingAsync(default)).Automatic);
        Assert.Single(f.Order.Payments); // Upload intake alone cannot create a payment.
        await f.AssertOriginalPaymentOnly();
        using (var heartbeat = await client.PostAsJsonAsync(endpoint + "/heartbeat", new BankNotificationHeartbeat(2, 0, 503)))
            Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        Assert.Equal("Queued", monitor.State(device).State);
        // Duplicate and unparsed requests are still real authenticated contact.
        // They must not erase the last queue/error report or acknowledge money.
        foreach (var upload in new[] { body, body with { Content = "Thông báo chưa nhận diện" } })
        {
            await f.RefreshDevice();
            device.LastSeenAtUtc = DateTime.UtcNow.AddMinutes(-5); await f.Db.SaveChangesAsync();
            using var response = await client.PostAsJsonAsync(endpoint, upload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.GetProperty("accepted").GetBoolean());
            await f.RefreshDevice();
            Assert.True(device.LastSeenAtUtc > DateTime.UtcNow.AddMinutes(-1));
            Assert.Equal("Queued", monitor.State(device).State);
            Assert.Equal(2, monitor.State(device).QueuedCount);
            Assert.Equal(503, monitor.State(device).LastErrorCode);
        }
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        Assert.Single(f.Order.Payments);
        await f.AssertOriginalPaymentOnly();
        using (var invalidHeartbeat = await client.PostAsJsonAsync(endpoint + "/heartbeat", new BankNotificationHeartbeat(2, 3, 503)))
            Assert.Equal(HttpStatusCode.BadRequest, invalidHeartbeat.StatusCode);
        using (var browserPost = await client.PostAsync("/__tests/notification-csrf-control", new StringContent("")))
            Assert.Equal(HttpStatusCode.BadRequest, browserPost.StatusCode);
        using (var malformed = await client.PostAsync(endpoint, new StringContent("{broken-json", System.Text.Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var contactBeforeDenied = DateTime.UtcNow.AddMinutes(-5);
        var monitorBeforeDenied = monitor.State(device).LastContactAtUtc;
        await f.RefreshDevice();
        device.LastSeenAtUtc = contactBeforeDenied; await f.Db.SaveChangesAsync();
        client.DefaultRequestHeaders.Remove("X-GaoApp-Notification-Device");
        using (var anonymous = await client.PostAsJsonAsync(endpoint, body)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var anonymousHeartbeat = await client.PostAsJsonAsync(endpoint + "/heartbeat", new BankNotificationHeartbeat(0, 0, 0))) Assert.Equal(HttpStatusCode.Unauthorized, anonymousHeartbeat.StatusCode);
        client.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{device.Id}:{new string('C', 64)}");
        using (var wrongKey = await client.PostAsJsonAsync(endpoint, body)) Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
        client.DefaultRequestHeaders.Remove("X-GaoApp-Notification-Device");
        client.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{device.Id}:{token}");
        f.PlainClient.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{device.Id}:{token}");
        using (var plainHttp = await f.PlainClient.PostAsJsonAsync(endpoint, body)) Assert.Equal(HttpStatusCode.Unauthorized, plainHttp.StatusCode);
        device.Enabled = false; await f.Db.SaveChangesAsync();
        using (var revoked = await client.PostAsJsonAsync(endpoint, body)) Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using (var revokedHeartbeat = await client.PostAsJsonAsync(endpoint + "/heartbeat", new BankNotificationHeartbeat(0, 0, 0))) Assert.Equal(HttpStatusCode.Unauthorized, revokedHeartbeat.StatusCode);
        await f.RefreshDevice();
        Assert.Equal(contactBeforeDenied, device.LastSeenAtUtc);
        Assert.Equal("Revoked", monitor.State(device).State);
        device.Enabled = true; // Inspect the monitor's contact independently of revocation display.
        Assert.Equal(monitorBeforeDenied, monitor.State(device).LastContactAtUtc);
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        await f.AssertAuthenticatedIntakeOnly();
    }
}

[AllowAnonymous]
[Route("__tests/notification-csrf-control")]
public sealed class BankNotificationHttpCsrfControlController : Controller
{
    [HttpPost]
    public IActionResult Post() => Ok();
}

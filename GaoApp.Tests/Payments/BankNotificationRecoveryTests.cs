using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_manual_takeover_is_server_timed_and_survives_page_reload()
    {
        await using var f = await Fixture.Create();
        var (bridge, _, qrId) = await BridgeFixture(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.BeginManualReviewAsync(qrId, default));
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3); await f.Db.SaveChangesAsync();
        await bridge.BeginManualReviewAsync(qrId, default);
        var status = JsonSerializer.SerializeToElement(await bridge.WaitingStatusAsync(qrId, default));
        Assert.True(status.GetProperty("manual").GetBoolean());
        Assert.Equal(60, status.GetProperty("warningAfterSeconds").GetInt32());
        Assert.Equal(120, status.GetProperty("manualAfterSeconds").GetInt32());
        Assert.Null(qr.PaymentId); Assert.Equal("ManualReviewRequested", qr.NotificationConfirmationSource);
        // A fresh scoped service observes the persisted takeover marker.
        var reloaded = new BankNotificationBridgeService(f.Db, f.Runtime, new TestLocks(), f.Service, null!);
        Assert.True(JsonSerializer.SerializeToElement(await reloaded.WaitingStatusAsync(qrId, default)).GetProperty("manual").GetBoolean());
        f.Runtime.TerminalId = 99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.BeginManualReviewAsync(qrId, default));
    }
    [Fact]
    public async Task Notification_manual_takeover_blocks_late_auto_confirmation_and_cannot_feed_the_next_installment()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3); await f.Db.SaveChangesAsync();
        await bridge.BeginManualReviewAsync(qrId, default);
        var id = await bridge.AcceptAsync(device, Upload(), default);
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Equal("LateManualReview", (await f.Db.Set<BankNotificationReceipt>().SingleAsync()).ConfirmationSource);
        var wait = await f.Db.Set<BankNotificationWait>().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(id!.Value, wait.Id, true, default));
        await f.Service.ConfirmManualQrAsync(qrId, default);
        Assert.Equal("ManualReviewConfirmed", qr.NotificationConfirmationSource);
        var next = await f.Service.CreateQrAsync(new GaoApp.Application.DTOs.POS.OrderDraftDto { OrderId = 100 }, Installment(20000), await ManualService(f), default);
        await bridge.ArmAsync(next.Id, default);
        await bridge.AcceptAsync(device, Upload(), default); // lost ACK replay still belongs to old evidence
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Single(f.Order.Payments, x => x.Method == GaoApp.Domain.Enums.PaymentMethod.BankTransfer);
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync(x => x.Id == next.Id)).PaymentId);
        // A distinct strong request code can safely identify the next installment.
        await bridge.AcceptAsync(device, Upload(pos: "POS100 " + next.RequestCode, reference: "NEXT12345678"), default);
        var identified = Assert.Single(await bridge.PendingAsync(default));
        Assert.Equal(next.Id, identified.QrId); Assert.True(identified.Automatic);
        await bridge.ConfirmAsync(identified.ReceiptId, identified.WaitId, true, default);
        Assert.Equal(2, f.Order.Payments.Count(x => x.Method == GaoApp.Domain.Enums.PaymentMethod.BankTransfer));
    }
    [Fact]
    public async Task Notification_dismiss_pauses_auto_and_does_not_pause_a_different_amount()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var wrong = await bridge.AcceptAsync(device, Upload(10000, "Chuyen tien", "WRONG12345678"), default);
        var wait = await f.Db.Set<BankNotificationWait>().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.DismissAsync(wrong!.Value, wait.Id, default));
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync()).NotificationConfirmationSource);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        await bridge.DismissAsync(item.ReceiptId, item.WaitId, default);
        Assert.True(JsonSerializer.SerializeToElement(await bridge.WaitingStatusAsync(qrId, default)).GetProperty("manual").GetBoolean());
        await bridge.BeginManualReviewAsync(qrId, default); // Retry preserves an already authorized early rejection.
    }
    [Fact]
    public async Task Notification_heartbeat_reports_queue_without_acknowledging_money_or_changing_payments()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.HeartbeatAsync(device, new(3, 0, 503), default);
        var status = JsonSerializer.SerializeToElement(await bridge.WaitingStatusAsync(qrId, default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Queued", status.GetProperty("phones")[0].GetProperty("state").GetString());
        Assert.Equal(3, status.GetProperty("phones")[0].GetProperty("queuedCount").GetInt32());
        Assert.Single(f.Order.Payments); Assert.Empty(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.HeartbeatAsync(device, new(3, 4, 0), default));
    }
}

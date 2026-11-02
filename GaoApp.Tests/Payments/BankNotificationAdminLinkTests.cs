using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static async Task<BankNotificationUpload> IdentifiedAdminUpload(Fixture f, int qrId, string reference = "ADMINLINK12345")
    {
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync(q => q.Id == qrId);
        return Upload((int)qr.Amount, $"POS-{qr.OrderId}-{qr.RequestCode}", reference);
    }

    [Fact]
    public async Task Notification_admin_waiting_row_reuses_an_existing_phone_receipt_without_a_second_acknowledgement()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        var id = await bridge.AcceptAsync(device, await IdentifiedAdminUpload(f, qrId), default);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.AdminApproveAsync(qrId, 20000, null, default); // Retry the same waiting-row action.
        var pending = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(id, pending.ReceiptId); Assert.Equal(qrId, pending.QrId); Assert.True(pending.AwaitingTerminal);
        Assert.NotNull(pending.AppliedWaitId); Assert.Null(pending.PaymentId);
        Assert.Empty(await bridge.PendingAsync(default)); Assert.Single(f.Order.Payments);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var history = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal("NotificationAdmin", history.ConfirmationSource); Assert.NotNull(history.PaymentId);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Notification_phone_arriving_after_admin_acknowledgement_is_reserved_or_linked_to_its_existing_payment(bool alreadyRecorded, bool omitPos)
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        if (alreadyRecorded) await bridge.CompleteAdminApprovalAsync(qrId, default);
        var upload = await IdentifiedAdminUpload(f, qrId);
        if (omitPos) upload = upload with { Content = upload.Content.Replace("POS-100-", "") };
        var id = await bridge.AcceptAsync(device, upload, default);
        var receipt = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        Assert.Equal(id, qr.NotificationReceiptId); Assert.NotNull(receipt.AppliedWaitId);
        Assert.Equal(qr.PaymentId, receipt.PaymentId); Assert.Equal(7, receipt.ConfirmedByUserId);
        Assert.Equal(qr.ManualConfirmedAtUtc, receipt.ConfirmedAtUtc);
        Assert.Equal(alreadyRecorded ? "NotificationAdmin" : "NotificationAdminPending", receipt.ConfirmationSource);
        var row = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal("KHACH HANG THU", row.SenderName); Assert.Equal("GD:ADMINLINK12345", row.TransactionCode);
        Assert.Equal(!alreadyRecorded, row.AwaitingTerminal);
        Assert.Empty(await bridge.PendingAsync(default));
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var firstPaymentId = receipt.PaymentId;
        Assert.Equal(id, await bridge.AcceptAsync(device, upload, default));
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        Assert.Equal(firstPaymentId, receipt.PaymentId);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
        var nextQr = await NextNotificationOrder(f, bridge);
        var wait = await f.Db.Set<BankNotificationWait>().SingleAsync(w => w.QrId == nextQr);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(id!.Value, wait.Id, false, default));
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync(q => q.Id == nextQr)).PaymentId);
    }

    [Fact]
    public async Task Notification_old_unlinked_admin_history_is_one_received_transaction_and_reading_it_does_not_change_money()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        var upload = await IdentifiedAdminUpload(f, qrId);
        var legacy = new BankNotificationReceipt { StoreId = 1, DeviceId = device.Id, BankAccountId = 1, Amount = 20000,
            Content = upload.Content, OrderId = 100, QrCode = qr.RequestCode.ToUpperInvariant(), TransactionCode = "GD:ADMINLINK12345",
            SenderName = "KHACH THU", EvidenceKey = new string('E', 64), PostedAtUtc = DateTime.UtcNow, ConfirmationSource = "LateManualReview" };
        f.Db.Add(legacy); await f.Db.SaveChangesAsync();
        var row = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(qr.PaymentId, row.PaymentId); Assert.Equal(legacy.Id, row.ReceiptId);
        Assert.Equal("NotificationAdmin", row.ConfirmationSource); Assert.Equal("KHACH THU", row.SenderName);
        Assert.Null(legacy.PaymentId); Assert.Null(qr.NotificationReceiptId); // GET projection preserves audit storage.
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
        // A repeated terminal command durably links that same bank evidence.
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        Assert.Equal(qr.PaymentId, legacy.PaymentId); Assert.Equal(legacy.Id, qr.NotificationReceiptId);
        Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }

    [Theory]
    [InlineData("Chuyen tien", 20000)]
    [InlineData("POS101", 20000)]
    [InlineData("POS100", 10000)]
    [InlineData("POS100 QR-100-00112233445566778899AABBCCDDEEFF", 20000)]
    public async Task Notification_admin_does_not_consume_unidentified_or_mismatched_phone_money(string pos, int amount)
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        await bridge.AcceptAsync(device, Upload(amount, pos), default);
        var receipt = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Null(receipt.PaymentId); Assert.Null(receipt.AppliedWaitId);
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync()).NotificationReceiptId);
        Assert.Equal(2, (await bridge.AdminHistoryAsync(default)).Count);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }

    [Fact]
    public async Task Notification_POS_only_cannot_pick_between_two_installments_of_the_same_order_and_amount()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var next = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 100 }, Installment(20000), await ManualService(f), default);
        await bridge.ArmAsync(next.Id, default);
        await bridge.AcceptAsync(device, Upload(), default);
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync()).PaymentId);
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync(q => q.Id == qrId)).NotificationReceiptId);
        Assert.Empty(await bridge.PendingAsync(default));
    }

    [Fact]
    public async Task Notification_admin_explicitly_selecting_late_missing_POS_evidence_links_to_existing_money_without_charging_again()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var id = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        Assert.Equal(2, (await bridge.AdminHistoryAsync(default)).Count);
        await bridge.AdminApproveAsync(qrId, 20000, id, default);
        await bridge.AdminApproveAsync(qrId, 20000, id, default);
        var row = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(id, row.ReceiptId); Assert.Equal("NotificationAdmin", row.ConfirmationSource);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
        var other = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien", reference: "DIFFERENTBANK12345"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, other, default));
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync(r => r.Id == other)).PaymentId);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }
}

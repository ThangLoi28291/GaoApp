using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_admin_history_keeps_no_phone_approval_and_recorded_payment_once_without_fake_bank_evidence()
    {
        await using var f = await Fixture.Create();
        var (bridge, _, qrId) = await BridgeFixture(f);
        f.Db.Users.Add(new User { Id = 7, UserName = "admin-test", FullName = "Admin thử nghiệm", PasswordHash = "test" });
        await f.Db.SaveChangesAsync();
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        var pending = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(qrId, pending.QrId); Assert.Null(pending.ReceiptId);
        Assert.Equal(100, pending.OrderId); Assert.Equal(20000, pending.Amount);
        Assert.Equal("Test terminal", pending.TerminalName); Assert.Equal("Admin thử nghiệm", pending.ConfirmedByName);
        Assert.True(pending.AwaitingTerminal); Assert.Null(pending.PaymentId);
        Assert.Null(pending.SenderName); Assert.Null(pending.TransactionCode); Assert.Null(pending.Content);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        f.Db.ChangeTracker.Clear();
        var recorded = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(qrId, recorded.QrId); Assert.NotNull(recorded.PaymentId); Assert.False(recorded.AwaitingTerminal);
        Assert.Equal("AdminManualConfirmed", recorded.ConfirmationSource); Assert.Equal(7, recorded.ConfirmedByUserId);
        Assert.Empty(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        Assert.Single(await f.Db.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
        Assert.Equal(0, f.FinalizeCount); // History cannot close an underpaid order.
    }

    [Fact]
    public async Task Notification_admin_history_merges_linked_phone_evidence_and_fills_the_confirmed_order_without_duplicating_it()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        var receiptId = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        await bridge.AdminApproveAsync(qrId, 20000, receiptId, default);
        var pending = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.True(pending.AwaitingTerminal); Assert.Equal(receiptId, pending.ReceiptId); Assert.Equal(qrId, pending.QrId);
        Assert.Equal(100, pending.OrderId); Assert.Equal("KHACH HANG THU", pending.SenderName);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        var saved = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal("NotificationAdmin", saved.ConfirmationSource); Assert.NotNull(saved.PaymentId);
        Assert.False(saved.AwaitingTerminal); Assert.Equal("GD:TEST12345678", saved.TransactionCode);
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
    }

    [Fact]
    public async Task Notification_history_keeps_an_unidentified_late_receipt_separate_from_admin_money()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var rows = await bridge.AdminHistoryAsync(default);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, x => x.PaymentId.HasValue);
        Assert.Equal("LateManualReview", Assert.Single(rows, x => x.ReceiptId.HasValue).ConfirmationSource);
        Assert.Single(await f.Db.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
        f.Tenant.SetStore(2, "other");
        Assert.Empty(await bridge.AdminHistoryAsync(default));
    }

    [Fact]
    public async Task Notification_admin_history_does_not_call_cancelled_approval_a_recorded_payment()
    {
        await using var f = await Fixture.Create(); var (bridge, _, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        (await f.Db.PosPaymentQrRequests.SingleAsync()).Status = PosPaymentQrStatus.Cancelled;
        await f.Db.SaveChangesAsync();
        var row = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Null(row.PaymentId); Assert.False(row.AwaitingTerminal); Assert.Equal(PosPaymentQrStatus.Cancelled, row.QrStatus);
    }

    [Fact]
    public async Task Notification_cashier_history_shows_actual_applied_order_without_rewriting_the_original_missing_POS()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        var id = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var suggestion = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(suggestion.ReceiptId, suggestion.WaitId, false, default);
        var row = Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Equal(id, row.ReceiptId); Assert.Equal(100, row.OrderId); Assert.NotNull(row.PaymentId);
        Assert.Equal("KHACH HANG THU", row.SenderName); Assert.Equal("NotificationCashier", row.ConfirmationSource);
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync()).OrderId);
    }
}

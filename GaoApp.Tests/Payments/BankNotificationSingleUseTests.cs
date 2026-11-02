using System.Reflection;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static async Task<int> NextNotificationOrder(Fixture f, BankNotificationBridgeService bridge)
    {
        var order = new Order { Id = 101, StoreId = 1, GrandTotal = 100000, POSShiftId = 10 };
        order.Lines.Add(new OrderLine { StoreId = 1, Quantity = 1, UnitPrice = 100000, LineTotal = 100000,
            Variant = f.Order.Lines.Single().Variant });
        f.Db.Add(order); await f.Db.SaveChangesAsync();
        var qr = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 101 }, Installment(20000), await ManualService(f), default);
        await bridge.ArmAsync(qr.Id, default);
        return qr.Id;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Notification_used_transfer_cannot_pay_the_next_order_when_a_reference_is_added(bool admin, bool automatic)
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var upload = Upload(pos: automatic ? "POS100" : "Chuyen tien");
        var firstId = await bridge.AcceptAsync(device, upload, default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        if (admin)
        {
            await bridge.AdminApproveAsync(qrId, 20000, firstId, default);
            await bridge.CompleteAdminApprovalAsync(qrId, default);
        }
        else await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, automatic, default);
        var nextQrId = await NextNotificationOrder(f, bridge);
        var replay = upload with { Content = upload.Content + " FT998877665544" };
        Assert.Equal(firstId, await bridge.AcceptAsync(device, replay, default));
        Assert.Empty(await bridge.PendingAsync(default));
        var next = await f.Db.Set<BankNotificationWait>().SingleAsync(x => x.QrId == nextQrId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(firstId!.Value, next.Id, false, default));
        Assert.Single(await f.Db.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync(x => x.Id == nextQrId)).PaymentId);
    }

    [Fact]
    public async Task Notification_legacy_duplicate_evidence_is_blocked_in_pending_cashier_and_admin_paths()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        var upload = Upload(pos: "Chuyen tien");
        await bridge.AcceptAsync(device, upload, default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, false, default);
        var nextQrId = await NextNotificationOrder(f, bridge);
        var duplicate = new BankNotificationReceipt { StoreId = 1, DeviceId = device.Id, BankAccountId = 1,
            Amount = 20000, Content = upload.Content + " FT998877665544", TransactionCode = "FT998877665544",
            EvidenceKey = new string('B', 64), PayloadHash = new string('C', 64), PostedAtUtc = DateTime.UtcNow };
        f.Db.Add(duplicate); await f.Db.SaveChangesAsync();
        Assert.Empty(await bridge.PendingAsync(default));
        var next = await f.Db.Set<BankNotificationWait>().SingleAsync(x => x.QrId == nextQrId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(duplicate.Id, next.Id, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(nextQrId, 20000, duplicate.Id, default));
        Assert.Single(await f.Db.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
    }

    [Fact]
    public async Task Notification_stale_tracked_receipt_cannot_be_reassigned_after_another_request_confirmed_it()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        var nextQrId = await NextNotificationOrder(f, bridge);
        var receiptId = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        await using var staleDb = new TestContext((DbContextOptions<TestContext>)f.Db.GetService<IDbContextOptions>(), f.Tenant);
        await staleDb.Set<BankNotificationReceipt>().SingleAsync(x => x.Id == receiptId);
        var notifier = DispatchProxy.Create<IPosRealtimeNotifier, StubProxy>();
        ((StubProxy)(object)notifier).Call = (_, _) => Task.CompletedTask;
        var pos = DispatchProxy.Create<GaoApp.Application.Interfaces.Services.Orders.IPOSService, StubProxy>();
        ((StubProxy)(object)pos).Call = (_, _) => throw new InvalidOperationException("A replay must never finalize an order.");
        var stalePayments = new AcbPaymentService(staleDb, f.Protocol, pos, f.Runtime, notifier, new TestLocks());
        var staleBridge = new BankNotificationBridgeService(staleDb, f.Runtime, new TestLocks(), stalePayments, notifier);
        var oldWait = await f.Db.Set<BankNotificationWait>().SingleAsync(x => x.OrderId == 100);
        await bridge.ConfirmAsync(receiptId!.Value, oldWait.Id, false, default);
        var nextWait = await f.Db.Set<BankNotificationWait>().SingleAsync(x => x.QrId == nextQrId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => staleBridge.ConfirmAsync(receiptId.Value, nextWait.Id, false, default));
        Assert.Single(await staleDb.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
    }

    [Fact]
    public async Task Notification_MGD_repost_uses_bank_business_date_instead_of_new_Android_post_date()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var content = $"+20000 VND tu KHACH THU tai ACB Chuyen tien {yesterday.ToOffset(TimeSpan.FromHours(7)):ddMMyy-HH:mm:ss}. MGD: 31045";
        var upload = Upload() with { Content = content, PostedAtEpochMs = yesterday.ToUnixTimeMilliseconds() };
        var id = await bridge.AcceptAsync(device, upload, default);
        Assert.Equal(id, await bridge.AcceptAsync(device, upload with { PostedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, default));
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
    }

    [Fact]
    public async Task Notification_delayed_repost_after_manual_collection_cannot_feed_a_later_order()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddHours(-2); qr.ExpireAtUtc = DateTime.UtcNow.AddHours(-1);
        await f.Db.SaveChangesAsync();
        await bridge.BeginManualReviewAsync(qrId, default);
        await f.Service.ConfirmManualQrAsync(qrId, default);
        await NextNotificationOrder(f, bridge);
        var bankTime = DateTimeOffset.UtcNow.AddMinutes(-90).ToOffset(TimeSpan.FromHours(7));
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien") with { Content = Upload(pos: "Chuyen tien").Content + $" {bankTime:ddMMyy-HH:mm:ss}" }, default);
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Single(await f.Db.OrderPayments.Where(x => x.Method == PaymentMethod.BankTransfer).ToListAsync());
    }

    [Fact]
    public async Task Notification_a_distinct_transfer_for_the_same_amount_and_sender_can_pay_the_next_order()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var first = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(first.ReceiptId, first.WaitId, false, default);
        var nextQr = await NextNotificationOrder(f, bridge);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien", reference: "DIFFERENT12345") with {
            Content = Upload(pos: "Chuyen tien", reference: "DIFFERENT12345").Content.Replace("31021", "31022") }, default);
        var next = Assert.Single(await bridge.PendingAsync(default));
        Assert.Equal(nextQr, next.QrId); Assert.NotEqual(first.ReceiptId, next.ReceiptId); Assert.False(next.Automatic);
        await bridge.ConfirmAsync(next.ReceiptId, next.WaitId, false, default);
        Assert.Equal(2, await f.Db.OrderPayments.CountAsync(x => x.Method == PaymentMethod.BankTransfer));
        Assert.Equal(0, f.FinalizeCount); // Two real partial payments still do not close underpaid orders.
    }

    [Fact]
    public async Task Notification_legacy_unparsed_POS_is_rechecked_from_original_content_before_matching_another_order()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        var nextQr = await NextNotificationOrder(f, bridge);
        var raw = Upload(pos: "POS100" + qr.RequestCode.Replace("-", ""));
        // Older bridge versions saved the content, but missed its compact POS/QR.
        var legacy = new BankNotificationReceipt { StoreId = 1, DeviceId = device.Id, BankAccountId = 1, Amount = 20000,
            Content = raw.Content, TransactionCode = "GD:TEST12345678", EvidenceKey = new string('D', 64), PostedAtUtc = DateTime.UtcNow };
        f.Db.Add(legacy); await f.Db.SaveChangesAsync();
        var candidate = Assert.Single(await bridge.PendingAsync(default));
        Assert.Equal(100, candidate.OrderId); Assert.True(candidate.Automatic);
        await bridge.ConfirmAsync(candidate.ReceiptId, candidate.WaitId, true, default);
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync(x => x.Id == nextQr)).PaymentId);
        Assert.Null(legacy.OrderId); // Preserve the original audit fields instead of rewriting history.
    }

    [Fact]
    public async Task Notification_reference_is_scoped_to_the_receiving_account_even_when_bank_configuration_id_is_reused()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        var upload = Upload(pos: "Chuyen tien");
        await bridge.AcceptAsync(device, upload, default);
        var first = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(first.ReceiptId, first.WaitId, false, default);
        var bank = await f.Db.StoreBankAccounts.SingleAsync(); bank.AccountNumber = "DIFFERENT-RECEIVING-ACCOUNT";
        device.Enabled = false;
        var newDevice = new BankNotificationDevice { StoreId = 1, BankAccountId = bank.Id,
            BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "Other account phone", TokenHash = new string('C', 64) };
        f.Db.Add(newDevice); await f.Db.SaveChangesAsync();
        await NextNotificationOrder(f, bridge);
        var newReceiptId = await bridge.AcceptAsync(newDevice, upload, default);
        Assert.NotEqual(first.ReceiptId, newReceiptId);
        var next = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(next.ReceiptId, next.WaitId, false, default);
        Assert.Equal(2, await f.Db.OrderPayments.CountAsync(x => x.Method == PaymentMethod.BankTransfer));
    }
}

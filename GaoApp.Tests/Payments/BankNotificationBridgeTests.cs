using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static BankNotificationUpload Upload(int amount = 20000, string pos = "POS100", string reference = "TEST12345678", long? posted = null) => new("mobile.acb.com.vn",
        $"Cửa hàng nhận tiền thành công +{amount} VND từ KHACH HANG THU tại ACB - {pos} GD {reference} MGD: 31021", posted ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private static async Task<(BankNotificationBridgeService Bridge, BankNotificationDevice Device, int QrId)> BridgeFixture(Fixture f, decimal amount = 20000, BankNotificationMonitor? monitor = null, bool arm = true)
    {
        var manual = await ManualService(f);
        var bank = await f.Db.StoreBankAccounts.SingleAsync();
        var device = new BankNotificationDevice { StoreId = 1, BankAccountId = 1, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "Test phone", TokenHash = new string('A',64) };
        f.Db.Add(device);
        f.Db.Add(new POSTerminal { Id = 3, StoreId = 1, Code = "TEST3", Name = "Test terminal" });
        await f.Db.SaveChangesAsync();
        var notifier = DispatchProxy.Create<IPosRealtimeNotifier, StubProxy>();
        ((StubProxy)(object)notifier).Call = (m,a) => { f.Notifications.Add(((int)a![0]!, (string)a[1]!, (int)a[3]!)); return Task.CompletedTask; };
        var bridge = new BankNotificationBridgeService(f.Db, f.Runtime, new TestLocks(), f.Service, notifier, monitor);
        var qr = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 100 }, Installment(amount), manual, default);
        if (arm) await bridge.ArmAsync(qr.Id, default);
        return (bridge, device, qr.Id);
    }

    [Fact]
    public async Task Notification_delayed_arm_matches_money_received_after_QR_creation_and_before_the_browser_arms_it()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f, arm: false);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-30); await f.Db.SaveChangesAsync();
        var bankTime = DateTime.UtcNow.AddSeconds(-15);
        var upload = Upload(pos: "POS100 " + qr.RequestCode, posted: new DateTimeOffset(bankTime).ToUnixTimeMilliseconds());
        upload = upload with { Content = upload.Content + " " + bankTime.AddHours(7).ToString("ddMMyy-HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) };
        var receiptId = await bridge.AcceptAsync(device, upload, default);
        Assert.Empty(await bridge.PendingAsync(default)); // Upload arrived while /arm was unavailable.

        Assert.True(await bridge.ArmAsync(qrId, default));
        var wait = await f.Db.Set<BankNotificationWait>().SingleAsync();
        var occurred = BankNotificationParser.Parse(upload.Content)!.BankOccurredAtUtc!.Value;
        Assert.True(wait.CreatedAtUtc > occurred.AddSeconds(5)); // Keep the real registration/audit timestamp.
        Assert.True(qr.CreatedAtUtc <= occurred);
        var item = Assert.Single(await bridge.PendingAsync(default));
        Assert.True(item.Automatic);
        Assert.Equal(receiptId, item.ReceiptId);
        Assert.Equal(qrId, item.QrId);
        Assert.Equal(wait.Id, item.WaitId);
        Assert.Equal(20000, item.Amount);
        Assert.Single(f.Order.Payments); // Matching alone does not create money or finalize the order.
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync()).PaymentId);

        var confirmed = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        var repeated = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        Assert.False(confirmed.GetProperty("finalized").GetBoolean());
        Assert.False(repeated.GetProperty("finalized").GetBoolean());
        Assert.Equal(50000, confirmed.GetProperty("remainingAmount").GetDecimal());
        Assert.Equal(confirmed.GetProperty("paidTotal").GetDecimal(), repeated.GetProperty("paidTotal").GetDecimal());
        Assert.Equal(OrderStatus.Draft, f.Order.Status);
        Assert.Equal(0, f.FinalizeCount);
        var payment = Assert.Single(f.Order.Payments, x => x.Method == PaymentMethod.BankTransfer);
        Assert.Equal(20000, payment.Amount);
        var savedReceipt = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Equal(payment.Id, savedReceipt.PaymentId);
        Assert.Equal(wait.Id, savedReceipt.AppliedWaitId);
        Assert.Equal("NotificationAuto", savedReceipt.ConfirmationSource);
        Assert.Equal(payment.Id, qr.PaymentId);
        Assert.Equal(savedReceipt.Id, qr.NotificationReceiptId);
        Assert.Empty(await bridge.PendingAsync(default));
    }

    [Fact]
    public async Task Notification_delayed_arm_does_not_match_historical_money_from_before_QR_creation()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f, arm: false);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-30); await f.Db.SaveChangesAsync();
        var bankTime = DateTime.UtcNow.AddSeconds(-45);
        var upload = Upload(pos: "POS100 " + qr.RequestCode, posted: new DateTimeOffset(bankTime).ToUnixTimeMilliseconds());
        upload = upload with { Content = upload.Content + " " + bankTime.AddHours(7).ToString("ddMMyy-HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) };
        await bridge.AcceptAsync(device, upload, default);
        Assert.True(await bridge.ArmAsync(qrId, default));
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Single(f.Order.Payments);
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync()).PaymentId);
    }
    [Theory]
    [InlineData("POS100")]
    [InlineData("POS-100")]
    [InlineData("pos - 100")]
    public async Task Notification_POS_and_exact_waiting_amount_confirm_without_RequestCode_once(string pos)
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var receipt = await bridge.AcceptAsync(device, Upload(pos: pos), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        Assert.True(item.Automatic); Assert.Equal(qrId, item.QrId); Assert.Equal(receipt, item.ReceiptId);
        var result = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        Assert.False(result.GetProperty("finalized").GetBoolean());
        await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
        var saved = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Equal("NotificationAuto", saved.ConfirmationSource); Assert.NotNull(saved.PaymentId);
        Assert.Equal(saved.PaymentId, (await f.Db.PosPaymentQrRequests.SingleAsync()).PaymentId);
        Assert.Equal(saved.Id, (await f.Db.PosPaymentQrRequests.SingleAsync()).NotificationReceiptId);
        Assert.Empty(await bridge.PendingAsync(default));
    }
    [Theory]
    [InlineData(true, 20000)]
    [InlineData(false, 20000)]
    [InlineData(true, 30000)]
    public async Task Notification_joined_POS_QR_requires_exact_pending_QR_and_amount(bool sameQr, int amount)
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        var compactQr = (sameQr ? qr.RequestCode : "QR-100-00112233445566778899AABBCCDDEEFF").Replace("-", "");
        var receipt = await bridge.AcceptAsync(device, Upload(amount, "POS100" + compactQr), default);
        Assert.NotNull(receipt);
        var saved = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Equal(100, saved.OrderId);
        if (!sameQr || amount != 20000)
        {
            Assert.Empty(await bridge.PendingAsync(default));
            Assert.Single(f.Order.Payments);
            return;
        }
        var item = Assert.Single(await bridge.PendingAsync(default));
        Assert.True(item.Automatic); Assert.Equal(qrId, item.QrId);
        var result = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        Assert.False(result.GetProperty("finalized").GetBoolean());
        await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
        Assert.Equal(0, f.FinalizeCount);
    }
    [Theory]
    [InlineData(10000)]
    [InlineData(30000)]
    [InlineData(70000)]
    public async Task Notification_correct_POS_with_wrong_waiting_amount_never_confirms(int received)
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        var receipt = await bridge.AcceptAsync(device, Upload(received), default);
        Assert.Empty(await bridge.PendingAsync(default));
        var wait = await f.Db.Set<BankNotificationWait>().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(receipt!.Value, wait.Id, true, default));
        Assert.Single(f.Order.Payments); Assert.Equal(0, f.FinalizeCount);
    }
    [Fact]
    public async Task Notification_without_POS_requires_cashier_and_skip_keeps_manual_confirmation_available()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        Assert.False(item.Automatic); Assert.Equal("KHACH HANG THU", item.SenderName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        await bridge.DismissAsync(item.ReceiptId, item.WaitId, default); await bridge.DismissAsync(item.ReceiptId, item.WaitId, default);
        Assert.Empty(await bridge.PendingAsync(default));
        Assert.Null((await f.Db.Set<BankNotificationReceipt>().SingleAsync()).PaymentId);
        Assert.Single(await f.Db.Set<BankNotificationDismissal>().ToListAsync());
        await f.Service.ConfirmManualQrAsync(qrId, default);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }
    [Fact]
    public async Task Notification_without_POS_cashier_confirmation_records_the_sender_evidence()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, false, default);
        Assert.Equal("NotificationCashier", (await f.Db.Set<BankNotificationReceipt>().SingleAsync()).ConfirmationSource);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }
    [Fact]
    public async Task Notification_same_amount_at_two_terminals_can_only_be_consumed_once()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        f.Db.Add(new POSTerminal { Id = 4, StoreId = 1, Code = "TEST4", Name = "Other terminal" });
        var other = new Order { Id = 101, StoreId = 1, GrandTotal = 100000, POSShiftId = 11,
            POSShift = new POSShift { Id = 11, StoreId = 1, TerminalId = 4, Status = POSShiftStatus.Open } };
        other.Lines.Add(new OrderLine { StoreId = 1, Quantity = 1, UnitPrice = 100000, LineTotal = 100000, Variant = f.Order.Lines.Single().Variant });
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        f.Runtime.TerminalId = 4;
        var manual = await ManualService(f);
        var otherQr = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 101 }, Installment(20000), manual, default);
        await bridge.ArmAsync(otherQr.Id, default);
        await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        var four = Assert.Single(await bridge.PendingAsync(default)); Assert.Equal(101, four.OrderId); Assert.False(four.Automatic);
        f.Runtime.TerminalId = 3;
        var three = Assert.Single(await bridge.PendingAsync(default)); Assert.Equal(100, three.OrderId);
        await bridge.ConfirmAsync(three.ReceiptId, three.WaitId, false, default);
        f.Runtime.TerminalId = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(four.ReceiptId, four.WaitId, false, default));
        Assert.Empty(await bridge.PendingAsync(default)); Assert.Empty(other.Payments);
        Assert.Single(f.Order.Payments, p => p.Method == PaymentMethod.BankTransfer);
    }
    [Fact]
    public async Task Notification_duplicates_and_old_scans_do_not_create_extra_payments()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        var old = Upload(posted: DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds());
        var first = await bridge.AcceptAsync(device, old, default);
        Assert.Equal(first, await bridge.AcceptAsync(device, old, default));
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync()); Assert.Empty(await bridge.PendingAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AcceptAsync(device, old with { Content = old.Content.Replace("20000", "30000") }, default));
        Assert.Single(f.Order.Payments);
    }
    [Fact]
    public async Task Notification_cart_changes_and_wrong_terminal_block_confirmation()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        f.Runtime.TerminalId = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        f.Runtime.TerminalId = 3;
        f.Order.GrandTotal++; await f.Db.SaveChangesAsync();
        Assert.Empty(await bridge.PendingAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        Assert.Single(f.Order.Payments);
    }
    [Fact]
    public async Task Notification_full_payment_uses_existing_finalize_and_print_claim_once()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f, 70000);
        await bridge.AcceptAsync(device, Upload(70000), default);
        var item = Assert.Single(await bridge.PendingAsync(default));
        var one = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        var two = JsonSerializer.SerializeToElement(await bridge.ConfirmAsync(item.ReceiptId, item.WaitId, true, default));
        Assert.True(one.GetProperty("finalized").GetBoolean()); Assert.Equal(1, f.FinalizeCount);
        Assert.NotEqual(JsonValueKind.Null, one.GetProperty("printUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, two.GetProperty("printUrl").ValueKind);
    }
    [Theory]
    [InlineData("OTP 123456 +20000 VND POS100 MGD: 31021")]
    [InlineData("Đã ẩn nội dung thông báo nhạy cảm")]
    [InlineData("-20000 VND POS100 MGD: 31021")]
    [InlineData("+20000 VND POS100 POS101 MGD: 31021")]
    public async Task Notification_unsafe_or_ambiguous_content_is_not_persisted(string content)
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        Assert.Null(await bridge.AcceptAsync(device, Upload() with { Content = content }, default));
        Assert.Empty(await f.Db.Set<BankNotificationReceipt>().ToListAsync()); Assert.Single(f.Order.Payments);
    }
    [Fact]
    public void Notification_parser_keeps_optional_QR_and_sender_but_never_fabricates_POS()
    {
        var data = BankNotificationParser.Parse("Cửa hàng nhận tiền thành công +861.000 VND từ KHACH HANG THU tại ACB - POS-100-QR-100-00112233445566778899AABBCCDDEEFF GD TEST12345678 MGD: 31020");
        Assert.NotNull(data); Assert.Equal(861000, data.Amount); Assert.Equal(100, data.OrderId);
        Assert.Equal("QR-100-00112233445566778899AABBCCDDEEFF", data.QrCode);
        Assert.Equal("KHACH HANG THU", data.SenderName);
        var noPos = BankNotificationParser.Parse("+196.000 VND từ KHACH HANG THU tại ACB Chuyen tien GD TEST12345678 MGD: 31021");
        Assert.NotNull(noPos); Assert.Null(noPos.OrderId); Assert.Null(noPos.QrCode);
        Assert.NotNull(BankNotificationParser.Parse("+75.000 VND POS100 FT00000123456 GD TEST12345678 MGD: 31021"));
    }
    [Fact]
    public async Task Notification_device_endpoint_requires_https_token_enabled_device_and_same_store()
    {
        // Pure authentication inputs only: no native Accept or financial proof
        // can be manufactured by this deliberately unversioned InMemory bank.
        await using (var invalidVersion = await Fixture.Create())
        {
            var bank = await invalidVersion.Db.StoreBankAccounts.SingleAsync();
            var authToken = new string('D', 64);
            var authDevice = new BankNotificationDevice { StoreId = 1, BankAccountId = bank.Id,
                BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "Invalid version auth input",
                TokenHash = BankNotificationParser.Hash(authToken), Enabled = true };
            invalidVersion.Db.Add(authDevice); await invalidVersion.Db.SaveChangesAsync();
            var http = new DefaultHttpContext(); http.Request.Scheme = "https";
            http.Request.Headers["X-GaoApp-Notification-Device"] = $"{authDevice.Id}:{authToken}";
            byte[]?[] invalidInputs = [null, [], new byte[7]];
            foreach (var version in invalidInputs)
            {
                bank.RowVersion = version!; invalidVersion.Db.Entry(bank).Property(x => x.RowVersion).IsModified = true;
                await invalidVersion.Db.SaveChangesAsync();
                var stored = (await invalidVersion.Db.StoreBankAccounts.AsNoTracking().SingleAsync()).RowVersion;
                if (version is null) Assert.Null(stored); else Assert.Equal(version, stored);
                Assert.Equal(authDevice.Id, (await GaoApp.Web.Security.BankNotificationDeviceAuthentication.AuthenticateAsync(invalidVersion.Db, http, default))!.Id);
                Assert.Null(await GaoApp.Web.Security.BankNotificationDeviceAuthentication.AuthenticateContextAsync(invalidVersion.Db, http, default));
                Assert.Empty(await invalidVersion.Db.Set<BankNotificationReceipt>().ToListAsync());
                Assert.Empty(await invalidVersion.Db.Set<GaoApp.Domain.Storefront.StorefrontNotificationAttestation>().ToListAsync());
                Assert.Single(invalidVersion.Order.Payments);
                Assert.Equal(30_000m, (await invalidVersion.Db.OrderPayments.SingleAsync()).Amount);
            }
        }
        await using var f = await SqlNotificationFixture.Create(); var device = f.Device;
        var token = new string('A',64); device.TokenHash = BankNotificationParser.Hash(token); await f.Db.SaveChangesAsync();
        const string endpoint = "/api/bank-notifications"; var body = f.Body();
        f.PlainClient.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{device.Id}:{token}");
        using (var plain = await f.PlainClient.PostAsJsonAsync(endpoint, body)) Assert.Equal(System.Net.HttpStatusCode.Unauthorized, plain.StatusCode);
        f.SetToken(new string('B',64));
        using (var wrong = await f.Client.PostAsJsonAsync(endpoint, body)) Assert.Equal(System.Net.HttpStatusCode.Unauthorized, wrong.StatusCode);
        f.SetToken(token);
        f.SelectRequestStore(f.ForeignStoreId);
        using (var foreign = await f.Client.PostAsJsonAsync(endpoint, body)) Assert.Equal(System.Net.HttpStatusCode.Unauthorized, foreign.StatusCode);
        f.SelectRequestStore(f.StoreId);
        using (var accepted = await f.Client.PostAsJsonAsync(endpoint, body)) Assert.Equal(System.Net.HttpStatusCode.OK, accepted.StatusCode);
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        await f.AssertAuthenticatedIntakeOnly(); await f.RefreshDevice();
        device.Enabled = false; await f.Db.SaveChangesAsync();
        using (var disabled = await f.Client.PostAsJsonAsync(endpoint, body)) Assert.Equal(System.Net.HttpStatusCode.Unauthorized, disabled.StatusCode);
        Assert.Single(f.Order.Payments); await f.AssertOriginalPaymentOnly();
    }
    [Fact]
    public async Task Notification_changed_receiving_account_requires_registration_again_and_does_not_auto_confirm()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(), default);
        var bank = await f.Db.StoreBankAccounts.SingleAsync(); bank.AccountNumber = "OTHER-ACCOUNT"; await f.Db.SaveChangesAsync();
        Assert.Empty(await bridge.PendingAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AcceptAsync(device, Upload(reference:"OTHER1234567"), default));
        Assert.Single(f.Order.Payments);
    }
}

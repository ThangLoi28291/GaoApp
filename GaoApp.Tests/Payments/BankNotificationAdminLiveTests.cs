using System.Reflection;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_admin_live_state_reads_new_QR_and_receipt_without_writing_money()
    {
        await using var f = await Fixture.Create();
        var bridge = new BankNotificationBridgeService(f.Db, f.Runtime, new TestLocks(), f.Service, null!);
        using var services = new ServiceCollection().AddSingleton<ITenantContext>(f.Tenant).BuildServiceProvider();
        var controller = LiveController(f, bridge, new(), services);

        var empty = Assert.IsType<PartialViewResult>(await controller.LiveState(default));
        Assert.Equal("_LiveState", empty.ViewName);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BankNotificationDevice>>(empty.Model));
        Assert.Empty((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting);
        Assert.Empty((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History);

        var (writer, _, qrId) = await BridgeFixture(f, arm: false);
        await controller.LiveState(default);
        Assert.Equal(qrId, Assert.Single((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting).QrId);
        var device = await f.Db.Set<BankNotificationDevice>().SingleAsync();
        await writer.AcceptAsync(device, Upload(), default);

        var fresh = Assert.IsType<PartialViewResult>(await controller.LiveState(default));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<BankNotificationDevice>>(fresh.Model));
        var history = Assert.Single((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History);
        Assert.Equal(20000, history.Amount);
        Assert.Equal("KHACH HANG THU", history.SenderName);
        Assert.Single((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting);
        Assert.Single(f.Order.Payments); // Existing cash installment only; GET never accepts a transfer.
        Assert.Equal(0, f.FinalizeCount);
        Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync()).PaymentId);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Notification_admin_live_state_is_store_scoped_and_does_not_include_another_stores_rows()
    {
        await using var f = await Fixture.Create();
        var (bridge, _, _) = await BridgeFixture(f);
        f.Tenant.SetStore(2, "other");
        f.Db.StoreBankAccounts.Add(new StoreBankAccount { Id = 2, StoreId = 2, BankCode = "ACB", BankName = "Other bank",
            AccountNumber = "OTHER-ACCOUNT", AccountName = "Other store", IsActive = true });
        f.Db.Add(new BankNotificationDevice { Id = 99, StoreId = 2, BankAccountId = 2, Name = "Other device",
            TokenHash = new string('B', 64), BankAccountFingerprint = "OTHER" });
        f.Db.Add(new BankNotificationReceipt { Id = 99, StoreId = 2, DeviceId = 99, BankAccountId = 2, Amount = 999,
            Content = "OTHER STORE CONTENT", TransactionCode = "OTHER-REF", EvidenceKey = "OTHER-EVIDENCE", PayloadHash = "OTHER-HASH", PostedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Tenant.SetStore(1, "store1");
        using var services = new ServiceCollection().AddSingleton<ITenantContext>(f.Tenant).BuildServiceProvider();
        var controller = LiveController(f, bridge, new(), services);

        var result = Assert.IsType<PartialViewResult>(await controller.LiveState(default));
        Assert.All(Assert.IsAssignableFrom<IReadOnlyList<BankNotificationDevice>>(result.Model), row => Assert.Equal(1, row.StoreId));
        Assert.All((IReadOnlyList<StoreBankAccount>)controller.ViewBag.Accounts, row => Assert.Equal(1, row.StoreId));
        Assert.Empty((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History);

        f.Tenant.SetStore(2, "other");
        result = Assert.IsType<PartialViewResult>(await controller.LiveState(default));
        Assert.Equal(99, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<BankNotificationDevice>>(result.Model)).Id);
        Assert.Empty((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting);
        Assert.Equal("OTHER-REF", Assert.Single((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History).TransactionCode);
    }

    [Fact]
    public async Task Notification_admin_live_state_recomputes_phone_contact_state_on_every_read()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, _) = await BridgeFixture(f);
        device.LastSeenAtUtc = DateTime.UtcNow.AddMinutes(-3);
        await f.Db.SaveChangesAsync();
        var monitor = new BankNotificationMonitor();
        using var services = new ServiceCollection().AddSingleton<ITenantContext>(f.Tenant).BuildServiceProvider();
        var controller = LiveController(f, bridge, monitor, services);
        await controller.LiveState(default);
        Assert.Equal("Disconnected", ((IReadOnlyDictionary<int, BankNotificationPhoneState>)controller.ViewBag.PhoneStates)[device.Id].State);

        monitor.Report(1, device.Id, new(0, 0, 0));
        await controller.LiveState(default);
        Assert.Equal("Connected", ((IReadOnlyDictionary<int, BankNotificationPhoneState>)controller.ViewBag.PhoneStates)[device.Id].State);
        Assert.Single(f.Order.Payments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notification_admin_live_state_keeps_approved_QR_in_history_until_terminal_records_payment(bool withReceipt)
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var receiptId = withReceipt ? await bridge.AcceptAsync(device, Upload(), default) : null;
        using var services = new ServiceCollection().AddSingleton<ITenantContext>(f.Tenant).BuildServiceProvider();
        var controller = LiveController(f, bridge, new(), services);

        await bridge.AdminApproveAsync(qrId, 20000, receiptId, default);
        await controller.LiveState(default);
        var waiting = Assert.Single((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting);
        Assert.True(waiting.Approved);
        var history = Assert.Single((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History);
        Assert.Equal(qrId, history.QrId);
        Assert.True(history.AwaitingTerminal);
        Assert.Null(history.PaymentId);
        Assert.Equal(withReceipt ? "NotificationAdminPending" : "AdminApprovalPending", history.ConfirmationSource);
        Assert.Single(f.Order.Payments); // Sending an admin decision is not a recorded transfer.

        await bridge.CompleteAdminApprovalAsync(qrId, default);
        await controller.LiveState(default);
        Assert.Empty((IReadOnlyList<BankNotificationAdminWait>)controller.ViewBag.Waiting);
        history = Assert.Single((IReadOnlyList<BankNotificationHistoryItem>)controller.ViewBag.History);
        Assert.Equal(qrId, history.QrId);
        Assert.False(history.AwaitingTerminal);
        Assert.NotNull(history.PaymentId);
        Assert.Equal(withReceipt ? "NotificationAdmin" : "AdminManualConfirmed", history.ConfirmationSource);
        Assert.Single(f.Order.Payments, payment => payment.Method == GaoApp.Domain.Enums.PaymentMethod.BankTransfer);
        Assert.Equal(0, f.FinalizeCount); // Partial payment follows the normal fully-paid finalization rule.
    }

    [Fact]
    public async Task Notification_admin_live_state_keeps_a_new_confirmation_of_an_older_receipt_in_the_latest_history()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync();
        qr.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30);
        await f.Db.SaveChangesAsync();
        var posted = DateTimeOffset.UtcNow.AddMinutes(-20);
        var receiptId = await bridge.AcceptAsync(device, Upload(posted: posted.ToUnixTimeMilliseconds()), default);
        for (var i = 0; i < 41; i++)
            f.Db.Add(new BankNotificationReceipt { StoreId = 1, DeviceId = device.Id, BankAccountId = 1, Amount = 12345,
                Content = $"Cửa hàng nhận tiền thành công +12345 VND từ OTHER SENDER tại ACB - Chuyen tien GD NEW{i:D10}",
                TransactionCode = $"GD:NEW{i:D10}", EvidenceKey = $"NEW-EVIDENCE-{i}", PayloadHash = $"NEW-HASH-{i}",
                PostedAtUtc = DateTime.UtcNow.AddMinutes(-1) });
        await f.Db.SaveChangesAsync();
        Assert.DoesNotContain(await bridge.AdminHistoryAsync(default), row => row.ReceiptId == receiptId);

        await bridge.AdminApproveAsync(qrId, 20000, receiptId, default);
        var history = await bridge.AdminHistoryAsync(default);
        var first = history[0];
        Assert.Equal(receiptId, first.ReceiptId);
        Assert.Equal(qrId, first.QrId);
        Assert.True(first.AwaitingTerminal);
        Assert.Equal(posted.ToUnixTimeMilliseconds(), new DateTimeOffset(DateTime.SpecifyKind(first.PostedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
        Assert.Null(first.PaymentId);
        Assert.Single(f.Order.Payments);
    }

    [Fact]
    public void Notification_admin_live_state_keeps_bank_view_authorization_and_disables_caching()
    {
        Assert.Equal(PermissionCodes.System.BankAccount.View,
            typeof(BankNotificationsController).GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy);
        var action = typeof(BankNotificationsController).GetMethod(nameof(BankNotificationsController.LiveState))!;
        Assert.Equal("live-state", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        var cache = action.GetCustomAttribute<ResponseCacheAttribute>()!;
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
        Assert.DoesNotContain(action.GetParameters(), p => p.Name is "storeId" or "terminalId");
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    private static BankNotificationsController LiveController(Fixture f, BankNotificationBridgeService bridge,
        BankNotificationMonitor monitor, IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        return new(f.Db, monitor, bridge, new TestLocks())
        {
            ControllerContext = new() { HttpContext = http },
            TempData = new TempDataDictionary(http, new SettingsTempDataProvider())
        };
    }
}

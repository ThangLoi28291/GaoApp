using System.Reflection;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_admin_approval_is_durable_and_only_original_terminal_applies_partial_payment_once()
    {
        await using var f = await Fixture.Create();
        var (bridge, device, qrId) = await BridgeFixture(f);
        var receiptId = await bridge.AcceptAsync(device, Upload(pos: "Chuyen tien"), default);
        f.Runtime.TerminalId = 99; // Admin is not operating the cashier terminal.
        Assert.False(Assert.Single(await bridge.AdminWaitingAsync(default)).Approved);
        await bridge.AdminApproveAsync(qrId, 20000, receiptId, default);
        Assert.True(Assert.Single(await bridge.AdminWaitingAsync(default)).Approved);
        await bridge.AdminApproveAsync(qrId, 20000, receiptId, default); // Lost response retry.
        Assert.Single(f.Order.Payments); Assert.Equal(0, f.FinalizeCount);
        Assert.Equal("NotificationAdminPending", (await f.Db.PosPaymentQrRequests.SingleAsync()).NotificationConfirmationSource);
        Assert.Empty(await bridge.AdminApprovalsAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.CompleteAdminApprovalAsync(qrId, default));
        f.Runtime.TerminalId = 3;
        Assert.Equal(qrId, Assert.Single(await bridge.AdminApprovalsAsync(default)));
        Assert.Empty(await bridge.PendingAsync(default)); // Reserved evidence cannot be confirmed by the ordinary suggestion.
        var result = JsonSerializer.SerializeToElement(await bridge.CompleteAdminApprovalAsync(qrId, default));
        Assert.False(result.GetProperty("finalized").GetBoolean()); Assert.Equal(50000, result.GetProperty("remainingAmount").GetDecimal());
        await bridge.CompleteAdminApprovalAsync(qrId, default);
        Assert.Single(f.Order.Payments, x => x.Method == PaymentMethod.BankTransfer);
        Assert.Empty(await bridge.AdminApprovalsAsync(default));
        var receipt = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.NotNull(receipt.PaymentId); Assert.Equal("NotificationAdmin", receipt.ConfirmationSource); Assert.Equal(7, receipt.ConfirmedByUserId);
        Assert.Equal(receipt.PaymentId, (await f.Db.PosPaymentQrRequests.SingleAsync()).PaymentId);
        Assert.All(f.Notifications, x => Assert.Equal((1, "3", 100), x));
    }
    [Fact]
    public async Task Notification_admin_without_phone_upload_finishes_only_when_fully_paid_and_late_notification_cannot_feed_next_QR()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f, 70000);
        device.Enabled = false; await f.Db.SaveChangesAsync();
        await bridge.AdminApproveAsync(qrId, 70000, null, default);
        Assert.Empty(await f.Db.Set<BankNotificationReceipt>().ToListAsync()); Assert.Single(f.Order.Payments);
        var first = JsonSerializer.SerializeToElement(await bridge.CompleteAdminApprovalAsync(qrId, default));
        var repeat = JsonSerializer.SerializeToElement(await bridge.CompleteAdminApprovalAsync(qrId, default));
        Assert.True(first.GetProperty("finalized").GetBoolean()); Assert.Equal(1, f.FinalizeCount);
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("printUrl").ValueKind); Assert.Equal(JsonValueKind.Null, repeat.GetProperty("printUrl").ValueKind);
        Assert.Single(f.Order.Payments, x => x.Method == PaymentMethod.BankTransfer);
        device.Enabled = true; await f.Db.SaveChangesAsync();
        await bridge.AcceptAsync(device, Upload(70000), default);
        var receipt = await f.Db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Equal("NotificationAdmin", receipt.ConfirmationSource);
        Assert.Equal((await f.Db.PosPaymentQrRequests.SingleAsync()).PaymentId, receipt.PaymentId);
        Assert.Single(await bridge.AdminHistoryAsync(default));
        Assert.Empty(await bridge.PendingAsync(default));
    }
    [Fact]
    public async Task Notification_admin_checks_exact_amount_POS_QR_time_store_and_cart_before_approval()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 10000, null, default));
        var wrongPos = await bridge.AcceptAsync(device, Upload(pos: "POS101"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, wrongPos, default));
        var wrongQr = await bridge.AcceptAsync(device, Upload(pos: "POS100 QR-100-00112233445566778899AABBCCDDEEFF", reference: "WRONGQR123456"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, wrongQr, default));
        var old = await bridge.AcceptAsync(device, Upload(reference: "OLD123456789", posted: DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds()), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, old, default));
        f.Tenant.SetStore(2, "other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, null, default));
        f.Tenant.SetStore(1, "store1"); f.Order.GrandTotal++; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.AdminApproveAsync(qrId, 20000, null, default));
        Assert.Single(f.Order.Payments); Assert.Null((await f.Db.PosPaymentQrRequests.SingleAsync()).NotificationConfirmationSource);
    }
    [Fact]
    public async Task Notification_admin_can_explicitly_override_manual_pause_but_cart_change_after_approval_blocks_payment()
    {
        await using var f = await Fixture.Create(); var (bridge, device, qrId) = await BridgeFixture(f);
        var qr = await f.Db.PosPaymentQrRequests.SingleAsync(); qr.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3); await f.Db.SaveChangesAsync();
        await bridge.BeginManualReviewAsync(qrId, default);
        var id = await bridge.AcceptAsync(device, Upload(), default);
        await bridge.AdminApproveAsync(qrId, 20000, id, default);
        f.Order.GrandTotal++; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.CompleteAdminApprovalAsync(qrId, default));
        Assert.Single(f.Order.Payments); Assert.Null(qr.PaymentId);
    }
    [Fact]
    public async Task Notification_admin_delivery_reloads_a_QR_cancelled_by_another_request_before_recording_money()
    {
        await using var f = await Fixture.Create(); var (bridge, _, qrId) = await BridgeFixture(f);
        await bridge.AdminApproveAsync(qrId, 20000, null, default);
        var tracked = await f.Db.PosPaymentQrRequests.SingleAsync();
        await using var concurrent = new TestContext((DbContextOptions<TestContext>)f.Db.GetService<IDbContextOptions>(), f.Tenant);
        var cancelled = await concurrent.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId);
        cancelled.Status = PosPaymentQrStatus.Cancelled; await concurrent.SaveChangesAsync();
        Assert.Equal(PosPaymentQrStatus.Pending, tracked.Status); // Scoped context still has stale state.
        Assert.Empty(await bridge.AdminApprovalsAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.CompleteAdminApprovalAsync(qrId, default));
        Assert.Single(f.Order.Payments); Assert.Null(tracked.PaymentId); Assert.Equal(PosPaymentQrStatus.Cancelled, tracked.Status);
    }
    [Fact]
    public async Task Notification_admin_renew_keeps_device_identity_and_evidence_but_invalidates_previous_key()
    {
        await using var f = await Fixture.Create(); var (bridge, device, _) = await BridgeFixture(f);
        await bridge.AcceptAsync(device, Upload(), default);
        device.Enabled = false; device.TokenHash = BankNotificationParser.Hash(new string('A', 64)); await f.Db.SaveChangesAsync();
        var http = new DefaultHttpContext(); http.Request.Scheme = "https"; http.Request.Host = new HostString("store.example");
        using var services = new ServiceCollection().AddSingleton<GaoApp.Application.Common.ITenantContext>(f.Tenant).BuildServiceProvider();
        http.RequestServices = services;
        var controller = new BankNotificationsController(f.Db, new(), bridge, new TestLocks()) { ControllerContext = new() { HttpContext = http } };
        var file = Assert.IsType<FileContentResult>(await controller.Renew(device.Id, default));
        var config = JsonSerializer.Deserialize<JsonElement>(file.FileContents);
        Assert.Equal(device.Id, config.GetProperty("deviceId").GetInt32()); Assert.True(device.Enabled);
        Assert.Equal(BankNotificationParser.Hash(config.GetProperty("token").GetString()!), device.TokenHash);
        Assert.NotEqual(BankNotificationParser.Hash(new string('A', 64)), device.TokenHash);
        Assert.Single(await f.Db.Set<BankNotificationReceipt>().ToListAsync());
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }
    [Fact]
    public void Notification_admin_approval_and_POS_completion_require_payment_permissions_and_CSRF()
    {
        var approve = typeof(BankNotificationsController).GetMethod(nameof(BankNotificationsController.Approve))!;
        Assert.NotNull(approve.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var policies = approve.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ToArray();
        Assert.Contains(GaoApp.Application.Common.Security.PermissionCodes.System.BankAccount.Manage, policies);
        Assert.Contains(GaoApp.Application.Common.Security.PermissionCodes.Pos.Payment.Create, policies);
        Assert.Contains(GaoApp.Application.Common.Security.PermissionCodes.Pos.Order.Finalize, policies);
        var complete = typeof(PosBankNotificationsController).GetMethod(nameof(PosBankNotificationsController.CompleteAdmin))!;
        Assert.NotNull(complete.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal(GaoApp.Application.Common.Security.PermissionCodes.Pos.Order.Finalize, complete.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }
}

using System.Net;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class BankNotificationHistoryPageTests
{
    [Fact]
    public async Task Admin_history_page_reads_existing_paid_QR_without_phone_and_updates_after_terminal_records_money()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var admin = await app.LoginAsync(account);
        await admin.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var cart = await admin.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = cart.GetProperty("orderId").GetInt32();
        int qrId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST-HISTORY", AccountName = "TEST" };
            db.Add(bank); await db.SaveChangesAsync();
            var qr = new PosPaymentQrRequest { StoreId = store.StoreId, OrderId = orderId, BankAccountId = bank.Id, Amount = 105000,
                RequestCode = $"QR-{orderId}-{Guid.NewGuid():N}", ConfirmMode = BankQrConfirmMode.Manual, QrRenderMode = BankQrRenderMode.LocalEmvQr,
                ManualConfirmedByUserId = account.UserId, ManualConfirmedAtUtc = DateTime.UtcNow,
                NotificationConfirmationSource = "AdminApprovalPending", ExpireAtUtc = DateTime.UtcNow.AddMinutes(10) };
            db.Add(qr); await db.SaveChangesAsync(); qrId = qr.Id;
        }
        var pending = await admin.Http.GetStringAsync("/admin/bank-notifications");
        Assert.Contains("Lịch sử nhận và xác nhận chuyển khoản", pending);
        Assert.Contains("Admin đã gửi xác nhận — chờ quầy ghi nhận", System.Net.WebUtility.HtmlDecode(pending));
        using (var live = await admin.Http.GetAsync("/admin/bank-notifications/live-state"))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.True(live.Headers.CacheControl?.NoStore);
            Assert.Equal("text/html", live.Content.Headers.ContentType?.MediaType);
            var fragment = WebUtility.HtmlDecode(await live.Content.ReadAsStringAsync());
            Assert.Contains("Admin đã gửi xác nhận — chờ quầy ghi nhận", fragment);
            Assert.Contains("bankNotificationLiveState", fragment);
            Assert.DoesNotContain("<html", fragment, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bridgeName", fragment); // A refresh cannot replace the configuration form.
            Assert.DoesNotContain("TokenHash", fragment);
            var waitingRows = Regex.Match(fragment, "<tbody id=\"bankNotificationWaitingRows\"[^>]*>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains("Không có khoản chuyển khoản đang chờ.", waitingRows);
            Assert.DoesNotContain($"qr-{qrId}", waitingRows);
        }
        var state = await admin.JsonAsync(HttpMethod.Get, "/admin/bank-notifications/history-state");
        Assert.Equal(qrId, Assert.Single(state.GetProperty("pendingQrIds").EnumerateArray()).GetInt32());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId);
            var payment = new OrderPayment { StoreId = store.StoreId, OrderId = orderId, Method = PaymentMethod.BankTransfer, Amount = 105000, ReferenceCode = qr.RequestCode };
            db.Add(payment); await db.SaveChangesAsync();
            qr.PaymentId = payment.Id; qr.Status = PosPaymentQrStatus.ManualConfirmed; qr.NotificationConfirmationSource = "AdminManualConfirmed";
            await db.SaveChangesAsync();
            Assert.Empty(await db.Set<BankNotificationReceipt>().ToListAsync());
        }
        var html = System.Net.WebUtility.HtmlDecode(await admin.Http.GetStringAsync("/admin/bank-notifications"));
        Assert.Contains($"POS-{orderId}", html);
        Assert.Contains("Admin xác nhận — đã ghi nhận tiền", html);
        Assert.Contains("Không có thông báo điện thoại", html);
        Assert.Contains("Người xác nhận: E2E test user", html);
        var liveRecorded = WebUtility.HtmlDecode(await admin.Http.GetStringAsync("/admin/bank-notifications/live-state"));
        Assert.Contains("Admin xác nhận — đã ghi nhận tiền", liveRecorded);
        Assert.DoesNotContain("Admin đã gửi xác nhận — chờ quầy ghi nhận", liveRecorded);
        var recorded = await admin.JsonAsync(HttpMethod.Get, "/admin/bank-notifications/history-state");
        Assert.Empty(recorded.GetProperty("pendingQrIds").EnumerateArray());
        // Existing deployments can already contain the two rows shown in the
        // report: a recorded admin acknowledgement and its unlinked phone upload.
        int lateId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId);
            var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == qr.BankAccountId);
            var order = await db.Orders.Include(x => x.POSShift).Include(x => x.Lines).ThenInclude(x => x.Variant)
                .Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
            var fingerprint = BankNotificationParser.AccountFingerprint(bank);
            var device = new BankNotificationDevice { StoreId = store.StoreId, BankAccountId = bank.Id, BankAccountFingerprint = fingerprint,
                Name = "Test phone", TokenHash = new string('A', 64) };
            db.Add(device);
            db.Add(new BankNotificationWait { StoreId = store.StoreId, QrId = qrId, OrderId = orderId, BankAccountId = bank.Id,
                BankAccountFingerprint = fingerprint, TerminalId = store.TerminalId, ShiftId = order.POSShiftId, Amount = qr.Amount,
                CartFingerprint = AcbPaymentPolicy.Fingerprint(order), ExpiresAtUtc = qr.ExpireAtUtc });
            await db.SaveChangesAsync();
            var receipt = new BankNotificationReceipt { StoreId = store.StoreId, BankAccountId = bank.Id, DeviceId = device.Id,
                Amount = qr.Amount, OrderId = orderId, QrCode = qr.RequestCode.ToUpperInvariant(), SenderName = "KHACH CHUYEN KHOAN",
                Content = $"+105000 VND từ KHACH CHUYEN KHOAN tại ACB POS-{orderId}-{qr.RequestCode} GD HISTORY12345 MGD: 31045",
                TransactionCode = "GD:HISTORY12345", EvidenceKey = new string('F', 64), PostedAtUtc = DateTime.UtcNow,
                ConfirmationSource = "LateManualReview" };
            db.Add(receipt); await db.SaveChangesAsync(); lateId = receipt.Id;
        }
        var merged = WebUtility.HtmlDecode(await admin.Http.GetStringAsync("/admin/bank-notifications"));
        Assert.Single(Regex.Matches(merged, $"<span>POS-{orderId}</span>"));
        Assert.Single(Regex.Matches(merged, "Admin xác nhận — đã ghi nhận tiền"));
        Assert.Contains("KHACH CHUYEN KHOAN", merged); Assert.Contains("GD:HISTORY12345", merged);
        Assert.DoesNotContain("Đến muộn / cần đối soát khoản thủ công", merged);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Single(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
            Assert.Null((await db.Set<BankNotificationReceipt>().SingleAsync(x => x.Id == lateId)).PaymentId);
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            order.Status = OrderStatus.Completed; order.GrandTotal = order.Subtotal = order.PaidTotal = 105000;
            order.PaymentStatus = PaymentStatus.Paid;
            await db.SaveChangesAsync();
        }
        // Real SQL writes attach to the pre-existing payment; delivery retries
        // cannot charge again, finalize again or claim another receipt print.
        var repeated = await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/bank-notifications/complete-admin/{qrId}", new { });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, repeated.GetProperty("printUrl").ValueKind);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var payment = Assert.Single(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
            Assert.Equal(payment.Id, (await db.Set<BankNotificationReceipt>().SingleAsync(x => x.Id == lateId)).PaymentId);
            Assert.Equal(lateId, (await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId)).NotificationReceiptId);
        }
        using var unauthorized = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var denied = await unauthorized.Http.GetAsync("/admin/bank-notifications/history-state");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var liveDenied = await unauthorized.Http.GetAsync("/admin/bank-notifications/live-state");
        Assert.Equal(HttpStatusCode.Forbidden, liveDenied.StatusCode);

        using var observer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.BankAccount.View));
        var viewOnly = WebUtility.HtmlDecode(await observer.Http.GetStringAsync("/admin/bank-notifications/live-state"));
        Assert.Contains("KHACH CHUYEN KHOAN", viewOnly);
        Assert.DoesNotContain("data-bank-approval", viewOnly);
        Assert.DoesNotContain("Thu hồi kết nối", viewOnly);

        using var otherStore = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], PermissionCodes.System.BankAccount.View));
        var isolated = WebUtility.HtmlDecode(await otherStore.Http.GetStringAsync("/admin/bank-notifications/live-state"));
        Assert.DoesNotContain("KHACH CHUYEN KHOAN", isolated);
        Assert.DoesNotContain("GD:HISTORY12345", isolated);
        Assert.DoesNotContain($"POS-{orderId}</span>", isolated);
    }
}

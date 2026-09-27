using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using GaoApp.Web.Services.Acb;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosOfflineSqlServerTests
{
    [Fact]
    public async Task Offline_status_and_bootstrap_identify_missing_or_closed_shifts_as_business_conflicts()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await AssertShiftRequired();
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var status = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/status");
        Assert.True(status.GetProperty("shiftId").GetInt32() > 0);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync();
            shift.Status = POSShiftStatus.Closed;
            await db.SaveChangesAsync();
        }
        await AssertShiftRequired();

        async Task AssertShiftRequired()
        {
            foreach (var endpoint in new[] { "status", "bootstrap" })
            {
                using var response = await client.Http.GetAsync("/admin/pos/offline/" + endpoint);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("POS_SHIFT_NOT_OPEN", problem.GetProperty("errorCode").GetString());
                Assert.Contains("Mở ca", problem.GetProperty("actionHint").GetString());
            }
        }
    }

    [Fact]
    public async Task Retried_cart_mutations_and_checkout_commit_once_with_durable_responses()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var create = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send(client, "/admin/pos/cart/current/new", new { }, create)));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.Status));
        var orderId = results[0].Body.GetProperty("orderId").GetInt32();
        Assert.All(results, x => Assert.Equal(orderId, x.Body.GetProperty("orderId").GetInt32()));
        var add = Guid.NewGuid();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await Send(client, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3", new { }, add)).Status);
        var altered = await Send(client, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=4", new { }, add);
        Assert.Equal(HttpStatusCode.Conflict, altered.Status);
        var paid = Guid.NewGuid();
        var body = new { orderId, clientRequestId = Guid.NewGuid(), method = 0, amount = 60 };
        var payments = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send(client, "/admin/pos/cart/current/payment-and-finalize", body, paid)));
        Assert.All(payments, x => Assert.Equal(HttpStatusCode.OK, x.Status));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(3, (await db.OrderLines.SingleAsync(x => x.OrderId == orderId)).Quantity);
        Assert.Single(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
        Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync(x => x.Id == orderId)).Status);
        Assert.Equal(97, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        Assert.Equal(3, await db.Set<PosOperationReceipt>().CountAsync());
        await using var outer = await db.Database.BeginTransactionAsync();
        var unit = new GaoApp.Infrastructure.Data.AppUnitOfWork(db);
        await using var participant = await unit.BeginTransactionAsync();
        await participant.RollbackAsync();
        Assert.Throws<InvalidOperationException>(unit.EnsureCanCommit);
        await outer.RollbackAsync();
    }

    [Fact]
    public async Task Offline_manual_transfer_checks_amount_context_and_preserves_manual_confirmation()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var orderId = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        int bankId, shiftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = new StoreBankAccount { StoreId = store.StoreId, AccountName = "TEST", AccountNumber = "123456789", BankCode = "ACB", BankName = "TEST", VietQrBankBin = "970416" };
            db.Set<StoreBankAccount>().Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            shiftId = (await db.POSShifts.SingleAsync()).Id;
        }
        var bootstrap = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/bootstrap");
        Assert.Equal(shiftId, bootstrap.GetProperty("shiftId").GetInt32());
        Assert.DoesNotContain("Secret", bootstrap.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var request = new { orderId, clientRequestId = Guid.NewGuid(), bankAccountId = bankId, amount = 60, referenceCode = "GAO-OFFLINE-TEST" };
        var failed = await Send(client, "/admin/pos/offline/manual-transfer", request, Guid.NewGuid(), shiftId, 59);
        Assert.Equal(HttpStatusCode.Conflict, failed.Status);
        await using (var check = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Empty(await check.OrderPayments.ToListAsync());
            Assert.Equal(OrderStatus.Draft, (await check.Orders.SingleAsync(x => x.Id == orderId)).Status);
            Assert.Equal(100, (await check.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, "/admin/pos/offline/manual-transfer", request, Guid.NewGuid(), shiftId + 1234, 60)).Status);
        var key = Guid.NewGuid();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await Send(client, "/admin/pos/offline/manual-transfer", request, key, shiftId, 60)).Status);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        var payment = await final.OrderPayments.SingleAsync(x => x.OrderId == orderId);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Contains("offline-manual", payment.MetadataJson);
        Assert.Equal(60, (await final.POSShifts.SingleAsync()).NonCashSalesTotal);
        Assert.Single(await final.Set<PosOperationReceipt>().ToListAsync());
    }

    [Fact]
    public async Task Existing_automatic_QR_can_be_manually_received_offline_and_late_confirmation_cannot_duplicate_it()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var orderId = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        int qrId, bankId, shiftId;
        await using (var seed = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await seed.Orders.Include(x => x.Lines).Include(x => x.POSShift).SingleAsync(x => x.Id == orderId);
            shiftId = order.POSShiftId;
            var bank = new StoreBankAccount { StoreId = store.StoreId, AccountName = "TEST", AccountNumber = "123456789", BankCode = "ACB", BankName = "TEST", VietQrBankBin = "970416" };
            seed.Add(bank); await seed.SaveChangesAsync(); bankId = bank.Id;
            var qr = new PosPaymentQrRequest { StoreId = store.StoreId, OrderId = orderId, BankAccountId = bankId, Amount = 60,
                RequestCode = "QR-OFFLINE-AUTO", QrRenderMode = BankQrRenderMode.ProviderApi, ConfirmMode = BankQrConfirmMode.Callback,
                ExpireAtUtc = DateTime.UtcNow.AddMinutes(20), Status = PosPaymentQrStatus.Pending };
            seed.Add(qr); await seed.SaveChangesAsync(); qrId = qr.Id;
            seed.Add(new AcbQrSession { StoreId = store.StoreId, OrderId = orderId, QrRequestId = qrId, ShiftId = shiftId,
                TerminalId = store.TerminalId, CashierId = account.UserId, Amount = 60, ProviderOrderId = "ACB-OFFLINE-AUTO",
                TraceNumber = "OFFLINE-TRACE", Status = AcbSessionStatus.Pending, CartFingerprint = AcbPaymentPolicy.Fingerprint(order) });
            await seed.SaveChangesAsync();
        }
        var body = new { orderId, existingQrId = qrId, bankAccountId = bankId, amount = 60, referenceCode = "QR-OFFLINE-AUTO", clientRequestId = Guid.NewGuid() };
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(client, "/admin/pos/offline/manual-transfer", body, Guid.NewGuid(), shiftId, 60)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        // The usual bank completion endpoint sees the same recorded receipt, with manual provenance intact.
        var late = await client.JsonAsync(HttpMethod.Post, $"/admin/acb/payments/{qrId}/complete", new { });
        Assert.True(late.GetProperty("finalized").GetBoolean());
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var payment = Assert.Single(await check.OrderPayments.ToListAsync());
        var session = await check.Set<AcbQrSession>().SingleAsync();
        Assert.Equal(payment.Id, session.PaymentId);
        Assert.Equal(AcbConfirmationSource.OfflineManual, session.ConfirmationSource);
        Assert.Equal(account.UserId, session.ConfirmedByUserId);
        Assert.Equal(AcbSessionStatus.Completed, session.Status);
        Assert.Equal(97, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.True((await check.Orders.SingleAsync(x => x.Id == orderId)).CompletedAtUtc < DateTime.UtcNow.AddMinutes(-4));
    }

    [Theory]
    [InlineData(InvoiceIssuanceRoute.Automatic)]
    [InlineData(InvoiceIssuanceRoute.Manual)]
    public async Task Invoice_intent_replay_is_idempotent_for_completed_order_after_current_cart_advances(InvoiceIssuanceRoute route)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var orderId = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        int shiftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            shiftId = (await db.POSShifts.SingleAsync()).Id;
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/admin/pos/cart/current/payment-and-finalize",
            new { orderId, clientRequestId = Guid.NewGuid(), method = 0, amount = 60 }, Guid.NewGuid(), shiftId, 60)).Status);
        DateTime? completedAt;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            // A delayed replay must not restart the public two-hour information window.
            order.CompletedAtUtc = DateTime.UtcNow.AddHours(-3);
            await db.SaveChangesAsync(); completedAt = order.CompletedAtUtc;
        }
        var nextId = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { })).GetProperty("orderId").GetInt32();
        Assert.NotEqual(orderId, nextId);
        var operationId = Guid.NewGuid();
        for (var retry = 0; retry < 2; retry++)
        {
            var response = await Send(client, $"/admin/pos/{orderId}/invoice-route", new { route }, operationId, shiftId);
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(orderId, response.Body.GetProperty("orderId").GetInt32());
            Assert.Equal(route.ToString(), response.Body.GetProperty("route").GetString());
        }
        var other = route == InvoiceIssuanceRoute.Manual ? InvoiceIssuanceRoute.Automatic : InvoiceIssuanceRoute.Manual;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, $"/admin/pos/{orderId}/invoice-route", new { route = other }, Guid.NewGuid(), shiftId)).Status);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var final = await check.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(route, final.InvoiceIssuanceRoute);
        Assert.Equal(completedAt, final.CompletedAtUtc);
        Assert.Equal(InvoiceIssuanceRoute.Unselected, (await check.Orders.SingleAsync(x => x.Id == nextId)).InvoiceIssuanceRoute);
        Assert.Single(await check.Set<PosOperationReceipt>().Where(x => x.OperationId == operationId).ToListAsync());
    }

    [Fact]
    public async Task Invoice_intent_rejects_an_order_outside_the_originating_shift_without_recording_success()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        int currentShiftId, orderId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            currentShiftId = (await db.POSShifts.SingleAsync()).Id;
            var oldShift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId,
                OpenedByUserId = account.UserId, WarehouseId = store.WarehouseId, Status = POSShiftStatus.Closed,
                OpenedAtUtc = DateTime.UtcNow.AddDays(-1), ClosedAtUtc = DateTime.UtcNow.AddHours(-4) };
            db.Add(oldShift); await db.SaveChangesAsync();
            var order = new Order { StoreId = store.StoreId, POSShiftId = oldShift.Id,
                Status = OrderStatus.Completed, CompletedAtUtc = DateTime.UtcNow.AddHours(-4) };
            db.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
        }
        var key = Guid.NewGuid();
        var response = await Send(client, $"/admin/pos/{orderId}/invoice-route",
            new { route = InvoiceIssuanceRoute.Manual }, key, currentShiftId);
        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(InvoiceIssuanceRoute.Unselected, (await check.Orders.SingleAsync(x => x.Id == orderId)).InvoiceIssuanceRoute);
        Assert.Empty(await check.Set<PosOperationReceipt>().Where(x => x.OperationId == key).ToListAsync());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Send(FullApplicationFixture.Client client,
        string path, object body, Guid key, int? shiftId = null, decimal? expected = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-POS-Operation-Id", key.ToString());
        if (shiftId.HasValue)
        {
            request.Headers.Add("X-POS-Shift-Id", shiftId.Value.ToString());
            request.Headers.Add("X-POS-Offline", "1");
            request.Headers.Add("X-POS-Occurred-At", DateTime.UtcNow.AddMinutes(-5).ToString("O"));
        }
        if (expected.HasValue) request.Headers.Add("X-POS-Expected-Total", expected.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await client.Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/json", $"{path}: {(int)response.StatusCode} {text}");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }
}

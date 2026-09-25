using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Cash_first_creates_only_remaining_amount_and_reuses_same_pending_QR()
    {
        await using var f = await Fixture.Create();
        var first = await f.Service.TryCreateAsync(100, default);
        var second = await f.Service.TryCreateAsync(100, default);
        Assert.Equal(70000m, first!.Amount);
        Assert.True(first.AutomaticConfirmation);
        Assert.Equal(first.Id, second!.Id);
        Assert.Single(f.Bank.Orders);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Equal((1, 10, 3, 100), (session.StoreId, session.ShiftId, session.TerminalId, session.OrderId));
    }

    [Fact]
    public async Task Gift_without_input_invoice_prevents_automatic_QR()
    {
        await using var f = await Fixture.Create();
        var gift = new ProductVariant { StoreId = 1, Sku = "TEST", HasInputInvoice = false };
        f.Order.Lines.Add(new OrderLine { StoreId = 1, OrderId = 100, Variant = gift, Quantity = 1, IsPromotionGift = true });
        await f.Db.SaveChangesAsync();
        Assert.Null(await f.Service.TryCreateAsync(100, default));
        Assert.Empty(f.Bank.Orders);
    }

    [Fact]
    public async Task Duplicate_webhooks_and_completion_record_one_payment_and_claim_one_print()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback(); await f.Callback();
        Assert.Single(await f.Db.Set<AcbPaymentTransaction>().ToListAsync());
        Assert.Equal(AcbSessionStatus.Received, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Single(f.Order.Payments); // Bank receipt alone does not add a second payment.
        var first = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(qr!.Id, default));
        var second = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(qr.Id, default));
        Assert.Equal(100, first.GetProperty("orderId").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("printUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("printUrl").ValueKind);
        Assert.Single(f.Order.Payments, x => x.Provider == "ACB");
        Assert.Equal(100000, f.Order.PaidTotal);
        Assert.Equal(1, f.FinalizeCount);
        Assert.All(f.Notifications, c => Assert.Equal((1, "3", 100), c));
    }

    [Theory]
    [InlineData(60000)]
    [InlineData(80000)]
    public async Task Amount_mismatch_is_preserved_for_review_without_payment_or_finalize(int received)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, received);
        await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(received, (await f.Db.Set<AcbPaymentTransaction>().SingleAsync()).Amount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
        Assert.Equal(0, f.FinalizeCount);
        Assert.Single(f.Order.Payments);
    }

    [Fact]
    public async Task Cancelled_QR_keeps_cash_and_late_money_requires_review()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        Assert.True(await f.Service.CancelAsync(qr!.Id, default));
        Assert.Equal(30000, f.Order.Payments.Single().Amount);
        Assert.Equal(OrderStatus.Draft, f.Order.Status);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Cancellation_racing_a_bank_payment_is_rejected()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CancelAsync(qr!.Id, default));
        Assert.Equal(0, f.Bank.CancelCalls);
    }

    [Fact]
    public async Task Changed_cart_does_not_auto_finalize()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        f.Order.Lines.Single().Quantity = 2;
        await f.Db.SaveChangesAsync();
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Wrong_terminal_cannot_complete_or_cancel_another_terminals_QR()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000); await f.Callback();
        f.Runtime.TerminalId = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CancelAsync(qr!.Id, default));
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Tenant_change_cannot_read_or_complete_another_stores_QR()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Tenant.SetStore(2, "other");
        f.Db.ChangeTracker.Clear();
        Assert.Null(await f.Service.SettingsAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.LookupAsync(100, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
    }

    [Fact]
    public async Task Finalization_guard_blocks_manual_bypass_of_unconfirmed_QR()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new AcbFinalizeGuard(f.Db).ValidateAsync(f.Order, default));
    }

    [Fact]
    public async Task Lookup_refresh_updates_bank_evidence_without_finalizing_or_printing()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Service.LookupAsync(100, true, default);
        Assert.Equal(0, f.FinalizeCount);
        Assert.Single(f.Order.Payments);
        Assert.Null((await f.Db.Set<AcbQrSession>().SingleAsync()).PrintClaimedAtUtc);
    }

    [Fact]
    public void Credentials_are_protected_and_bound_to_the_store()
    {
        var protocol = new AcbProtocol(new HttpClient(), new EphemeralDataProtectionProvider());
        var protectedKey = protocol.Protect(1, "test-only-callback-key");
        Assert.DoesNotContain("test-only-callback-key", protectedKey);
        Assert.True(protocol.VerifyKey(new StoreAcbSettings { StoreId = 1, CallbackApiKeyProtected = protectedKey }, "test-only-callback-key"));
        Assert.False(protocol.VerifyKey(new StoreAcbSettings { StoreId = 1, CallbackApiKeyProtected = protectedKey }, "wrong"));
        Assert.Throws<CryptographicException>(() => protocol.Unprotect(2, protectedKey));
    }

    [Theory]
    [InlineData("http://sandbox.acb.com.vn/test")]
    [InlineData("https://evil.example/acb")]
    [InlineData("https://sandbox.acb.com.vn.evil.example")]
    [InlineData("https://sandbox.acb.com.vn:8443")]
    public void Credentials_cannot_be_sent_to_untrusted_endpoints(string url) =>
        Assert.Throws<InvalidOperationException>(() => AcbProtocol.ValidateUrl(url));

    [Fact]
    public async Task Closed_shift_routes_received_money_to_review()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        f.Order.POSShift.Status = POSShiftStatus.Closed;
        await f.Db.SaveChangesAsync();
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Lost_initiate_response_does_not_create_another_bank_request()
    {
        await using var f = await Fixture.Create();
        f.Bank.DropInitiateResponse = true;
        await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, default));
        var conflict = await Assert.ThrowsAsync<ConflictAppException>(() => f.Service.TryCreateAsync(100, default));
        Assert.Contains((await f.Db.Set<AcbQrSession>().SingleAsync()).ProviderOrderId, conflict.Message);
        Assert.Single(f.Bank.Orders);
        Assert.Equal(AcbSessionStatus.Creating, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_batch_preserves_transactions_for_every_order()
    {
        await using var f = await Fixture.Create();
        var other = new Order { Id = 101, StoreId = 1, GrandTotal = 70000, POSShiftId = 10, POSShift = f.Order.POSShift };
        other.Lines.Add(new OrderLine { StoreId = 1, Quantity = 1, UnitPrice = 70000, LineTotal = 70000, Variant = f.Order.Lines.Single().Variant });
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        await f.Service.TryCreateAsync(100, default); await f.Service.TryCreateAsync(101, default);
        foreach (var id in f.Bank.Orders.Keys) f.Bank.Pay(id, 70000);
        await f.Callback();
        Assert.Equal(2, await f.Db.Set<AcbPaymentTransaction>().CountAsync());
        Assert.All(await f.Db.Set<AcbQrSession>().ToListAsync(), x => Assert.Equal(AcbSessionStatus.Received, x.Status));
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Two_bank_transfers_are_preserved_and_require_review()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var record = f.Bank.Orders.Single().Value;
        record.Received = 40000; record.SecondReceived = 30000;
        await f.Callback();
        Assert.Equal(2, await f.Db.Set<AcbPaymentTransaction>().CountAsync());
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public TestContext Db = null!;
        public TenantContext Tenant = new();
        public Runtime Runtime = new();
        public BankHandler Bank = new();
        public AcbPaymentService Service = null!;
        public AcbProtocol Protocol = null!;
        public AcbCallbackInbox Inbox = null!;
        public AcbCallbackSignal Signal = new();
        public CallbackRecordingLogger CallbackLogs = new();
        public AcbCallbackDiagnostics Diagnostics = null!;
        public Order Order = null!;
        public int FinalizeCount;
        public List<(int, string, int)> Notifications = [];
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Tenant.SetStore(1, "store1");
            f.Diagnostics = new AcbCallbackDiagnostics(f.CallbackLogs);
            var options = new DbContextOptionsBuilder<TestContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            f.Db = new TestContext(options, f.Tenant);
            var protocol = f.Protocol = new AcbProtocol(new HttpClient(f.Bank), new EphemeralDataProtectionProvider());
            f.Db.StoreBankAccounts.Add(new StoreBankAccount { Id = 1, StoreId = 1, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST", AccountName = "Test", IsActive = true });
            f.Db.Add(new StoreAcbSettings { StoreId = 1, Enabled = true, BankAccountId = 1, ClientId = "test-client",
                ClientSecretProtected = protocol.Protect(1, "test-secret"), CallbackApiKeyProtected = protocol.Protect(1, "test-key"),
                XProviderId = "TEST", XOwnerNumber = "TEST", MerchantId = "TEST", VirtualAccountPrefix = "TEST", BeneficiaryName = "TEST" });
            f.Order = new Order { Id = 100, StoreId = 1, GrandTotal = 100000, POSShiftId = 10,
                POSShift = new POSShift { Id = 10, StoreId = 1, TerminalId = 3, Status = POSShiftStatus.Open } };
            f.Order.Lines.Add(new OrderLine { StoreId = 1, Quantity = 1, UnitPrice = 100000, LineTotal = 100000,
                Variant = new ProductVariant { StoreId = 1, Sku = "TEST", HasInputInvoice = true } });
            f.Order.Payments.Add(new OrderPayment { StoreId = 1, Method = PaymentMethod.Cash, Amount = 30000 });
            f.Db.Add(f.Order); await f.Db.SaveChangesAsync();
            var pos = DispatchProxy.Create<IPOSService, StubProxy>();
            ((StubProxy)(object)pos).Call = (m, a) => m.Name == nameof(IPOSService.FinalizeAsync) ? f.FinalizeOrder((int)a![0]!) : throw new NotSupportedException(m.Name);
            var notifier = DispatchProxy.Create<IPosRealtimeNotifier, StubProxy>();
            ((StubProxy)(object)notifier).Call = (m, a) =>
            {
                f.Notifications.Add(((int)a![0]!, (string)a[m.Name == "NotifyStoreAsync" ? 2 : 1]!, (int)a[3]!));
                return Task.CompletedTask;
            };
            f.Service = new AcbPaymentService(f.Db, protocol, pos, f.Runtime, notifier, new TestLocks());
            f.Inbox = new AcbCallbackInbox(f.Db, new TestLocks(), f.Service, f.Signal, f.Diagnostics);
            return f;
        }
        private async Task<OrderDraftDto> FinalizeOrder(int id)
        {
            Assert.Equal(100, id);
            await new AcbFinalizeGuard(Db).ValidateAsync(Order, default);
            FinalizeCount++; Order.Status = OrderStatus.Completed;
            await Db.SaveChangesAsync(); return new OrderDraftDto { OrderId = id };
        }
        public Task Callback() => Service.CallbackAsync(JsonSerializer.SerializeToElement(new
        {
            requestParameters = new { request = new { requestParams = new { transactions = Bank.Orders.Keys.Select(id => new { transactionStatus = "COMPLETED", amount = Bank.Orders[id].Amount, transactionEntityAttribute = new { custom4 = id } }) } } }
        }), default);
        public ValueTask DisposeAsync() { Signal.Dispose(); return Db.DisposeAsync(); }
    }

    public class StubProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }
    private sealed class Runtime : IPOSRuntimeContextAccessor
    {
        public int? StoreId => 1; public int? TerminalId { get; set; } = 3; public int? UserId => 7;
        public string? StoreName => "Test"; public string? TerminalName => "Test"; public string? TerminalCode => "3"; public string? UserName => "Cashier";
    }
    private sealed class CurrentUser : ICurrentUser
    {
        public int? UserId => 7; public string? UserName => "Test"; public int? TerminalId => 3; public string? TerminalCode => "3"; public bool IsAuthenticated => true;
    }
    private sealed class TestContext(DbContextOptions<TestContext> options, TenantContext tenant) : AppDbContext(options, tenant, new CurrentUser())
    {
        public bool FailReceiptWrites;
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailReceiptWrites && ChangeTracker.Entries<AcbCallbackReceipt>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("synthetic-secret-database-error");
            return base.SaveChangesAsync(cancellationToken);
        }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.FindProperty("RowVersion") is { } rowVersion) rowVersion.IsNullable = true;
        }
    }
    private sealed class TestLocks : IAcbOrderLockProvider, IAsyncDisposable
    {
        public Task<IAsyncDisposable> AcquireAsync(AppDbContext db, int orderId, CancellationToken ct) => Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class BankRecord
    {
        public string Trace = ""; public decimal Amount; public decimal? Received; public decimal? SecondReceived; public bool Cancelled;
    }
    private sealed class BankHandler : HttpMessageHandler
    {
        public Dictionary<string, BankRecord> Orders = [];
        public int CancelCalls;
        public int TokenCalls;
        public bool RejectToken;
        public string? TokenFailureBody;
        public HttpRequestException? TokenNetworkError;
        public int RetrieveCalls;
        public bool FailRetrieve;
        public HashSet<string> FailedRetrieveIds = [];
        public bool FailCancel;
        public bool DropInitiateResponse;
        public string? CancellationCode;
        public bool MissingRetrieveOrders;
        public bool MalformedRetrieve;
        public void Pay(string id, decimal amount) => Orders[id].Received = amount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
            {
                TokenCalls++;
                if (TokenNetworkError != null) throw TokenNetworkError;
                if (RejectToken) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(TokenFailureBody ?? "test-sensitive-bank-error") };
                body = new { access_token = "test-token" };
            }
            else
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                if (request.RequestUri.AbsolutePath.EndsWith("/initiate"))
                {
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    var p = json.RootElement.GetProperty("requestParameters");
                    var id = AcbProtocol.Text(p, "orderId");
                    Orders.Add(id, new BankRecord { Trace = AcbProtocol.Text(p, "traceNumber"), Amount = AcbProtocol.Money(p, "amount") });
                    if (DropInitiateResponse) throw new HttpRequestException("Simulated lost response after bank accepted QR.");
                    body = new { responseStatus = new { responseCode = "00000000" }, responseBody = new { traceNumber = Orders[id].Trace, virtualAccount = "TESTVA", qrDataUrl = "data:image/png;base64,dGVzdA==" } };
                }
                else if (request.Method == HttpMethod.Delete)
                {
                    if (FailCancel) throw new HttpRequestException("Simulated cancellation failure");
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    var id = AcbProtocol.Text(json.RootElement.GetProperty("requestParameters"), "orderId");
                    CancelCalls++;
                    if (CancellationCode != null || !Orders.ContainsKey(id))
                        return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { responseStatus = new { responseCode = CancellationCode ?? "30020402" } }) };
                    Orders[id].Cancelled = true;
                    body = new { responseStatus = new { responseCode = "00000000" }, responseBody = new { status = "SUCCESS" } };
                }
                else
                {
                    RetrieveCalls++;
                    if (FailRetrieve) throw new HttpRequestException("Simulated retrieve outage");
                    if (MalformedRetrieve) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("synthetic-secret-invalid-bank-json") };
                    var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                    var id = q["orderId"].ToString();
                    if (FailedRetrieveIds.Contains(id)) throw new HttpRequestException("Simulated outage for one order");
                    if (!Orders.TryGetValue(id, out var o))
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                        {
                            responseStatus = new { responseCode = "00000000" },
                            responseBody = new { pagination = new { totalPages = 0 }, orders = MissingRetrieveOrders ? null : Array.Empty<object>() }
                        }) };
                    body = new { responseStatus = new { responseCode = "00000000" }, responseBody = new
                    {
                        pagination = new { totalPages = 1 }, orders = new[] { new { orderId = id, traceNumber = o.Trace, amount = o.Amount,
                            status = o.Received.HasValue ? "COMPLETED" : o.Cancelled ? "CANCELLED" : "INIT",
                            transactionDetail = new[] { o.Received, o.SecondReceived }.Where(x => x.HasValue).Select((x, i) => new { transactionNumber = 123 + i, transactionAmount = x!.Value, transactionStatus = "COMPLETED", transactionContent = "Test payment", origPostDate = "2026-09-08" }).ToArray() } }
                    }};
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }
    }
}

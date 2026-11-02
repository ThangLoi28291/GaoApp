using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private sealed class LookupClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 23, 30, 0, TimeSpan.Zero); }

    private sealed class LookupBank(JsonNode body) : HttpMessageHandler
    {
        public int TokenCalls;
        public List<Uri> Queries = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? RawBody;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
            {
                ++TokenCalls;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "lookup-private-token" }) });
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal("/acb/open/payments/qr-payment/v1/retrieve", request.RequestUri.AbsolutePath);
            Assert.Equal("test-client", request.Headers.GetValues("X-Client-ID").Single());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Queries.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = RawBody == null ? JsonContent.Create(body) : new StringContent(RawBody) });
        }
    }

    private static JsonObject LookupResponse(params string[] orderIds) => new()
    {
        ["responseStatus"] = new JsonObject { ["responseCode"] = "00000000" },
        ["sensitiveNotForUi"] = "bank-private-sentinel",
        ["responseBody"] = new JsonObject
        {
            ["pagination"] = new JsonObject { ["totalRows"] = orderIds.Length, ["totalPages"] = orderIds.Length == 0 ? 0 : 1 },
            ["orders"] = new JsonArray(orderIds.Select(id => (JsonNode)new JsonObject
            {
                ["orderId"] = id, ["traceNumber"] = "fixture-trace", ["merchantId"] = "TEST",
                ["amount"] = 70000, ["status"] = "COMPLETED", ["createdDatetime"] = "2026-10-12T00:01:00.033+0700",
                ["transactionDetail"] = new JsonArray(new JsonObject { ["transactionNumber"] = 1234,
                    ["transactionAmount"] = 70000, ["transactionStatus"] = "COMPLETED",
                    ["transactionContent"] = "Thanh toán thử", ["origPostDate"] = "2026-10-12" })
            }).ToArray())
        }
    };

    private static async Task<(AcbBankLookupService Service, LookupBank Bank)> BankLookup(Fixture f, JsonObject response)
    {
        var bank = new LookupBank(response);
        var protocol = new AcbProtocol(new HttpClient(bank), new EphemeralDataProtectionProvider());
        var settings = await f.Db.Set<StoreAcbSettings>().SingleAsync();
        settings.ClientSecretProtected = protocol.Protect(1, "lookup-private-client-secret");
        await f.Db.SaveChangesAsync();
        return (new(f.Db, protocol, new LookupClock()), bank);
    }

    [Fact]
    public async Task Bank_range_lookup_uses_documented_paging_and_matches_only_local_orders_and_real_callbacks_without_writes()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var id = (await f.Db.Set<AcbQrSession>().SingleAsync()).ProviderOrderId;
        await f.Inbox.AcceptAsync(ListNotification(id), default);
        f.Tenant.SetStore(2, "store2");
        f.Db.Stores.Add(new Store { Id = 2, Name = "Foreign fixture", SubDomain = "store2", SubDomainNormalized = "STORE2" });
        var foreignShift = new POSShift { Id = 20, StoreId = 2, TerminalId = 23, OpenedByUserId = 7,
            Terminal = new POSTerminal { Id = 23, StoreId = 2, Code = "FOREIGN", Name = "Foreign fixture" } };
        f.Db.Add(new Order { Id = 200, StoreId = 2, POSShiftId = foreignShift.Id, POSShift = foreignShift, OrderNumber = "foreign-private-order" });
        f.Db.Add(new AcbQrSession { StoreId = 2, OrderId = 200, ProviderOrderId = "foreign-reference" });
        f.Db.Add(new AcbCallbackReceipt { StoreId = 2, ClientRequestId = Guid.NewGuid().ToString(),
            Items = [new() { StoreId = 2, ProviderOrderId = id }] });
        await f.Db.SaveChangesAsync();
        f.Tenant.SetStore(1, "store1");
        var response = LookupResponse(id, "foreign-reference");
        var (service, bank) = await BankLookup(f, response);
        var receiptCount = await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().CountAsync();
        var qrStatus = (await f.Db.Set<AcbQrSession>().SingleAsync()).Status;
        f.Db.ChangeTracker.Clear();
        var result = await service.ReadAsync(null, null, 1, default);
        var query = QueryHelpers.ParseQuery(Assert.Single(bank.Queries).Query);
        Assert.Equal("2026-10-12", query["fromDate"]);
        Assert.Equal("2026-10-12", query["toDate"]);
        Assert.Equal("TEST", query["merchantId"]);
        Assert.Equal("0", query["page"]);
        Assert.Equal("50", query["size"]);
        Assert.False(query.ContainsKey("orderId"));
        Assert.Equal(2, result.TotalOrders);
        Assert.Equal(2, result.TransactionCount);
        Assert.Equal(140000, result.PaidAmount);
        var row = result.Orders.Single(x => x.ProviderOrderId == id);
        Assert.Equal(100, row.OrderId);
        Assert.Equal(1, row.CallbackCount);
        Assert.Equal(new DateTime(2026, 10, 11, 17, 1, 0, 33, DateTimeKind.Utc), row.CreatedAtUtc);
        Assert.Null(result.Orders.Single(x => x.ProviderOrderId == "foreign-reference").OrderId);
        Assert.Equal(0, result.Orders.Single(x => x.ProviderOrderId == "foreign-reference").CallbackCount);
        Assert.Equal(receiptCount, await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().CountAsync());
        Assert.Empty(await f.Db.Set<AcbPaymentTransaction>().ToListAsync());
        Assert.Equal(30000, (await f.Db.OrderPayments.SingleAsync()).Amount);
        Assert.Equal(qrStatus, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.All(f.Db.ChangeTracker.Entries(), x => Assert.Equal(EntityState.Unchanged, x.State));
        Assert.Equal(0, f.FinalizeCount);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private", serialized);
        Assert.DoesNotContain("client-secret", serialized);
    }

    [Fact]
    public async Task Bank_lookup_supports_31_inclusive_days_and_jumping_to_page_8_without_loading_previous_pages()
    {
        await using var f = await Fixture.Create();
        var response = LookupResponse("page8");
        response["responseBody"]!["pagination"]!["totalRows"] = 388;
        response["responseBody"]!["pagination"]!["totalPages"] = 8;
        var (service, bank) = await BankLookup(f, response);
        var result = await service.ReadAsync(new(2026, 9, 12), new(2026, 10, 12), 8, default);
        Assert.Equal(8, result.Page);
        Assert.Equal(8, result.TotalPages);
        Assert.Equal(388, result.TotalOrders);
        Assert.Equal("7", QueryHelpers.ParseQuery(Assert.Single(bank.Queries).Query)["page"]);
        Assert.Equal(1, bank.TokenCalls);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData(2026, 9, 11, 1)]
    [InlineData(2026, 10, 13, 1)]
    [InlineData(2026, 10, 12, 0)]
    public async Task Invalid_bank_range_or_page_is_rejected_before_contacting_bank(int year, int month, int day, int page)
    {
        await using var f = await Fixture.Create();
        var (service, bank) = await BankLookup(f, LookupResponse());
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadAsync(new(year, month, day), new(2026, 10, 12), page, default));
        Assert.Empty(bank.Queries);
        Assert.Equal(0, bank.TokenCalls);
    }

    [Fact]
    public async Task Empty_bank_page_is_a_successful_query_and_does_not_count_as_a_callback()
    {
        await using var f = await Fixture.Create();
        var (service, bank) = await BankLookup(f, LookupResponse());
        var page = await service.ReadAsync(null, null, 1, default);
        Assert.Empty(page.Orders);
        Assert.Equal(0, page.TotalOrders);
        Assert.Equal(0, page.TransactionCount);
        Assert.Equal(0, page.PaidAmount);
        Assert.Single(bank.Queries);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("pages")]
    [InlineData("amount")]
    [InlineData("merchant")]
    [InlineData("duplicate")]
    [InlineData("time")]
    public async Task Malformed_bank_evidence_is_not_rendered_as_a_successful_lookup(string field)
    {
        await using var f = await Fixture.Create();
        var response = LookupResponse("fixture-order");
        var body = response["responseBody"]!;
        var order = body["orders"]![0]!;
        switch (field)
        {
            case "orders": body["orders"] = null; break;
            case "pages": body["pagination"]!["totalPages"] = -1; break;
            case "amount": order["transactionDetail"]![0]!["transactionAmount"] = "bank-private-sentinel"; break;
            case "merchant": order["merchantId"] = "foreign-private-merchant"; break;
            case "duplicate": ((JsonArray)order["transactionDetail"]!).Add(order["transactionDetail"]![0]!.DeepClone()); break;
            case "time": order["createdDatetime"] = "2026-10-12T00:01:00"; break;
        }
        var (service, _) = await BankLookup(f, response);
        var error = await Assert.ThrowsAsync<AcbApiException>(() => service.ReadAsync(null, null, 1, default));
        Assert.DoesNotContain("private", error.Message);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Bank_lookup_requires_current_stores_enabled_settings_and_active_account(bool disableAccount, bool wrongStore)
    {
        await using var f = await Fixture.Create();
        var (service, bank) = await BankLookup(f, LookupResponse());
        if (wrongStore) f.Tenant.SetStore(2, "store2");
        else if (disableAccount) (await f.Db.StoreBankAccounts.SingleAsync()).IsActive = false;
        else (await f.Db.Set<StoreAcbSettings>().SingleAsync()).Enabled = false;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(null, null, 1, default));
        Assert.Empty(bank.Queries);
        Assert.Equal(0, bank.TokenCalls);
    }
}

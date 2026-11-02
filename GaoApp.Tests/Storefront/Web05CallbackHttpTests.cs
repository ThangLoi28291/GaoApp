using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Controllers;
using GaoApp.Web.Middlewares;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05CallbackHttpTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("missing")][InlineData("wrong")][InlineData("conflicting")][InlineData("repeated")]
    public async Task Bank_header_authentication_precedes_malformed_body_and_cannot_write_inbox_or_money(string authentication)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await CallbackHost.StartAsync(p); using var client = await host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/acb/webhook") { Content = new StringContent("{ definitely malformed", System.Text.Encoding.UTF8, "application/json") };
        if (authentication != "missing") request.Headers.TryAddWithoutValidation("x-api-key", authentication == "wrong" ? "not-the-private-key" : Key(p));
        if (authentication == "conflicting") request.Headers.TryAddWithoutValidation("Authorization", "Bearer another-key");
        if (authentication == "repeated") { request.Headers.Remove("x-api-key"); request.Headers.TryAddWithoutValidation("x-api-key", new[] { Key(p), Key(p) }); }
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = p.Case.Context(); Assert.Empty(await db.Set<AcbCallbackReceipt>().ToListAsync()); Assert.Empty(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Fact]
    public async Task Authenticated_callback_ACKs_durable_inbox_before_any_bank_query_then_real_processor_retry_confirms_without_page_or_shift()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await CallbackHost.StartAsync(p); using var client = await host.Client(); client.DefaultRequestHeaders.Add("x-api-key", Key(p));
        var payload = Payload(p.Reference); var calls = p.Bank.RetrieveCalls; using var accepted = await client.PostAsJsonAsync("/api/acb/webhook", payload); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Equal(calls, p.Bank.RetrieveCalls);
        int id;
        await using (var db = p.Case.Context()) { id = (await db.Set<AcbCallbackReceipt>().SingleAsync()).Id; Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); }
        using var duplicate = await client.PostAsJsonAsync("/api/acb/webhook", payload); Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode); await using (var db = p.Case.Context()) Assert.Single(await db.Set<AcbCallbackReceipt>().ToListAsync());
        p.Bank.FailRetrieve = true; await host.Process(id); await using (var db = p.Case.Context()) { Assert.Single(await db.Set<AcbCallbackReceipt>().ToListAsync()); Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); }
        p.Bank.FailRetrieve = false; p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using var check = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Confirmed, (await check.StorefrontOrders.SingleAsync()).PaymentState); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
        Assert.Empty(await check.POSShifts.ToListAsync()); Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task New_callback_request_rechecks_rotated_key_and_same_page_changed_payload_is_conflict_without_overwriting_evidence()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await CallbackHost.StartAsync(p); using var client = await host.Client(); client.DefaultRequestHeaders.Add("x-api-key", Key(p));
        var id = Guid.NewGuid().ToString(); using (var accepted = await client.PostAsJsonAsync("/api/acb/webhook", Payload(p.Reference, id))) Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        string original; await using (var db = p.Case.Context()) { original = (await db.Set<AcbCallbackReceipt>().SingleAsync()).PayloadJson; await db.Set<StoreAcbSettings>().ExecuteUpdateAsync(x => x.SetProperty(s => s.CallbackApiKeyProtected, Web05Support.AdminProtocol(p.Case.AdminKeys).Protect(p.Case.StoreId, "rotated-synthetic-key"))); }
        using (var revoked = await client.PostAsJsonAsync("/api/acb/webhook", Payload(p.Reference))) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        client.DefaultRequestHeaders.Remove("x-api-key"); client.DefaultRequestHeaders.Add("x-api-key", "rotated-synthetic-key");
        using (var conflict = await client.PostAsJsonAsync("/api/acb/webhook", Payload(p.Reference, id, 1))) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var check = p.Case.Context(); Assert.Equal(original, (await check.Set<AcbCallbackReceipt>().SingleAsync()).PayloadJson); Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Fact]
    public async Task Public_guest_key_provider_cannot_authenticate_admin_protected_callback_credentials_or_send_to_bank()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await CallbackHost.StartAsync(p, guestKeys: true); using var client = await host.Client(); client.DefaultRequestHeaders.Add("x-api-key", Key(p)); var calls = p.Bank.Requests.Count;
        using var response = await client.PostAsJsonAsync("/api/acb/webhook", Payload(p.Reference)); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal(calls, p.Bank.Requests.Count);
        await using var db = p.Case.Context(); Assert.Empty(await db.Set<AcbCallbackReceipt>().ToListAsync());
    }

    private static string Key(Web05Prepared p) => "synthetic-callback-key-W05-" + p.Case.StoreId;
    private static JsonElement Payload(string reference, string? clientId = null, decimal amount = 45_000m) => JsonSerializer.SerializeToElement(new {
        requestTrace = "synthetic-W05-trace", requestDateTime = DateTimeOffset.UtcNow.ToString("O"), requestParameters = new {
            masterMeta = new { clientId = "00000000-0000-0000-0000-000000000001", clientRequestId = clientId ?? Guid.NewGuid().ToString(), checksum = "not-monetary-proof" },
            request = new { requestMeta = new { requestType = "NOTIFICATION", requestCode = "TRANSACTION_UPDATE" }, requestParams = new {
                transactions = new[] { new { transactionStatus = "COMPLETED", effectiveDate = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd"), amount, transactionEntityAttribute = new { custom1 = "SYNTHETIC-W05", custom2 = "WEB05-REGISTERED", custom4 = reference } } },
                pagination = new { page = 1, pageSize = 100, totalPage = 1 } } } } });

    private sealed class CallbackHost(WebApplication app, Web05Prepared prepared) : IAsyncDisposable
    {
        internal static async Task<CallbackHost> StartAsync(Web05Prepared p, bool guestKeys = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped<TenantContext>(); builder.Services.AddScoped<ITenantContext>(s => s.GetRequiredService<TenantContext>()); builder.Services.AddScoped<ITenantContextWriter>(s => s.GetRequiredService<TenantContext>());
            builder.Services.AddScoped<AppDbContext>(s => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(p.Case.Seed.Database.ConnectionString).Options, s.GetRequiredService<TenantContext>(), new Web01Actor(null, false)));
            builder.Services.AddSingleton(p.Bank); builder.Services.AddScoped<AcbProtocol>(_ => new(new HttpClient(p.Bank), guestKeys ? p.Case.Web.Protection() : Web05Support.AdminProtection(p.Case.AdminKeys)));
            builder.Services.AddScoped<IStorefrontBankProcessor>(s => new StorefrontBankProcessor(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<AcbProtocol>().Core, p.Case.Web.Clock));
            builder.Services.AddScoped<IPOSRuntimeContextAccessor, NoPosRuntime>(); builder.Services.AddScoped<IAcbOrderLockProvider, SqlAcbOrderLockProvider>();
            builder.Services.AddScoped<IPOSService>(_ => System.Reflection.DispatchProxy.Create<IPOSService, Web05Proxy>()); builder.Services.AddScoped<IPosRealtimeNotifier>(_ => { var value = System.Reflection.DispatchProxy.Create<IPosRealtimeNotifier, Web05Proxy>(); ((Web05Proxy)(object)value).Call = (_, _) => Task.CompletedTask; return value; });
            builder.Services.AddScoped<AcbPaymentService>(); builder.Services.AddScoped<AcbCallbackInbox>(); builder.Services.AddSingleton<AcbCallbackSignal>(); builder.Services.AddSingleton<AcbCallbackDiagnostics>();
            builder.Services.Configure<TenantOptions>(x => { x.RootDomain = "web05.invalid"; x.AdminSubdomain = "admin"; }); builder.Services.AddControllers().AddApplicationPart(typeof(AcbWebhookController).Assembly);
            var app = builder.Build(); app.UseMiddleware<AcbCallbackDiagnosticsMiddleware>(); app.UseMiddleware<TenantResolutionMiddleware>(); app.MapControllers(); await app.StartAsync(); return new(app, p);
        }
        internal async Task<HttpClient> Client()
        {
            var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            client.DefaultRequestHeaders.Host = await prepared.Case.Seed.SubdomainAsync() + ".web05.invalid"; return client;
        }
        internal async Task Process(int id)
        {
            await using var scope = app.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ITenantContextWriter>().SetStore(prepared.Case.StoreId, await prepared.Case.Seed.SubdomainAsync());
            await scope.ServiceProvider.GetRequiredService<AcbCallbackInbox>().ProcessAsync(id, default);
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
    private sealed class NoPosRuntime : IPOSRuntimeContextAccessor
    {
        public int? StoreId => null; public int? TerminalId => null; public int? UserId => null; public string? StoreName => null; public string? TerminalName => null; public string? TerminalCode => null; public string? UserName => null;
    }
}

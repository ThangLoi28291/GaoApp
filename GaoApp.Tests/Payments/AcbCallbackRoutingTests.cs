using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Configuration;
using GaoApp.Web.Controllers;
using GaoApp.Web.Middlewares;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static async Task AddRoutingStores(Fixture f, bool targetActive = true)
    {
        f.Tenant.SetStore(2, "www"); // Seed each store under its own tenant, never weaken the production ownership guard.
        f.Db.AddRange(
            new Store { Id = 1, Name = "Target store", SubDomain = "DoanTL", SubDomainNormalized = "doantl", IsActive = targetActive },
            new Store { Id = 2, Name = "Original www store", SubDomain = "www", SubDomainNormalized = "www", IsActive = true },
            new StoreAcbSettings { StoreId = 2, CallbackApiKeyProtected = f.Protocol.Protect(2, "other-store-key") });
        await f.Db.SaveChangesAsync();
        f.Tenant.SetStore(1, "doantl");
    }

    private static async Task<WebApplication> RoutingTestHost(Fixture f, string? alias = "www.gaomart.com.vn", int? target = 1)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<AppDbContext>(f.Db);
        builder.Services.AddSingleton<ITenantContextWriter>(f.Tenant);
        builder.Services.AddSingleton<ITenantContext>(f.Tenant);
        builder.Services.AddSingleton(f.Diagnostics);
        builder.Services.AddSingleton(f.Service);
        builder.Services.AddSingleton(f.Protocol);
        builder.Services.AddSingleton(f.Inbox);
        builder.Services.Configure<TenantOptions>(o => { o.RootDomain = "gaomart.com.vn"; o.AdminSubdomain = "admin"; });
        if (alias != null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AcbCallbackRouting:Hosts:0"] = alias

        });
        if (alias != null)
        {
            f.Db.Add(new AcbCallbackRoute { Host = alias.Trim().ToLowerInvariant(), TargetStoreId = target });
            await f.Db.SaveChangesAsync();
        }
        builder.Services.AddAcbCallbackRouting(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(AcbWebhookController).Assembly);
        var app = builder.Build();
        app.UseMiddleware<AcbCallbackDiagnosticsMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.MapControllers();
        app.MapGet("/admin/routing-test", (ITenantContext tenant) => new { tenant.StoreId });
        app.MapPost("/Admin/api-callback-extra", (ITenantContext tenant) => new { tenant.StoreId });
        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("www.gaomart.com.vn", "/Admin/api-callback", "TRANSACTION_UPDATE")]
    [InlineData("www.gaomart.com.vn", "/Admin/api-callback", "TRANSACTION_HISTORY")]
    [InlineData("gaomart.com.vn", "/Admin/api-callback", "TRANSACTION_UPDATE")]
    [InlineData("WWW.GAOMART.COM.VN", "/admin/API-CALLBACK/", "TRANSACTION_UPDATE")]
    [InlineData("www.gaomart.com.vn", "/api/acb/webhook", "TRANSACTION_UPDATE")]
    public async Task Old_callback_host_routes_to_target_store_with_its_key_and_keeps_durable_deduplication(string host, string route, string code)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        var qr = await f.Service.TryCreateAsync(100, default);
        await using var app = await RoutingTestHost(f, host);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = host;
        var payload = code == "TRANSACTION_HISTORY" ? ListNotification(qr!.RequestCode) : Notification(qr!.RequestCode);
        client.DefaultRequestHeaders.Add("x-api-key", "other-store-key");
        using var wrongKey = await client.PostAsJsonAsync(route, payload);
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", "test-key");
        // Neither query nor unsigned headers may override the server-owned store mapping.
        client.DefaultRequestHeaders.Add("X-Store-Id", "2");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "www.gaomart.com.vn");
        using var accepted = await client.PostAsJsonAsync(route + "?tenant=www&storeId=2", payload);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Null(accepted.Headers.Location);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, receipt.StoreId);
        Assert.Equal(payload.GetRawText(), receipt.PayloadJson);
        Assert.Equal(0, f.Bank.RetrieveCalls); // ACK only saves authenticated evidence.
        using var duplicate = await client.PostAsJsonAsync(route, payload);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Single(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("StoreId=1") && x.Contains("RoutedStoreId=1") && x.Contains("RequestHost=", StringComparison.Ordinal));
        Assert.DoesNotContain(f.CallbackLogs.Entries, x => x.Contains("test-key") || x.Contains("other-store-key"));
        f.Bank.Pay(qr!.RequestCode, 70000);
        await f.Inbox.ProcessAsync(receipt.Id, default);
        await f.Service.CompleteAsync(qr.Id, default);
        using var afterComplete = await client.PostAsJsonAsync(route, payload);
        Assert.Equal(HttpStatusCode.OK, afterComplete.StatusCode);
        await f.Inbox.ProcessAsync(receipt.Id, default);
        Assert.Single(f.Order.Payments, x => x.Provider == "ACB");
        Assert.Equal(1, f.FinalizeCount);
        Assert.Contains((1, "3", 100), f.Notifications);
        Assert.Equal(receipt.Id, (await f.Db.Set<AcbQrSession>().SingleAsync()).ConfirmationCallbackReceiptId);
    }

    [Theory]
    [InlineData(999, true)]
    [InlineData(1, false)]
    public async Task Configured_alias_with_unavailable_target_fails_closed_instead_of_using_www_store(int target, bool active)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f, active);
        await using var app = await RoutingTestHost(f, target: target);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        client.DefaultRequestHeaders.Add("x-api-key", "other-store-key");
        using var result = await client.PostAsJsonAsync("/Admin/api-callback", Notification("unknown"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("CALLBACK_STORE_NOT_FOUND") && x.Contains("RoutedStoreId=" + target));
    }

    [Fact]
    public async Task Alias_applies_only_to_exact_POST_callbacks_and_does_not_change_normal_tenant_routing()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        await using var app = await RoutingTestHost(f);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        var page = await client.GetFromJsonAsync<JsonElement>("/admin/routing-test");
        Assert.Equal(2, page.GetProperty("storeId").GetInt32());
        using var extra = await client.PostAsJsonAsync("/Admin/api-callback-extra", new { });
        Assert.Equal(2, (await extra.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storeId").GetInt32());
        using var getCallback = await client.GetAsync("/Admin/api-callback");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getCallback.StatusCode);
        Assert.Equal(2, f.Tenant.StoreId);
        client.DefaultRequestHeaders.Host = "doantl.gaomart.com.vn";
        page = await client.GetFromJsonAsync<JsonElement>("/admin/routing-test");
        Assert.Equal(1, page.GetProperty("storeId").GetInt32());
        client.DefaultRequestHeaders.Host = "admin.gaomart.com.vn";
        using var admin = await client.GetAsync("/admin/routing-test");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.True(f.Tenant.IsHostAdmin);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("www.gaomart.com.vn.attacker.test", HttpStatusCode.BadRequest)]
    [InlineData("gaomart.com.vn", HttpStatusCode.BadRequest)]
    [InlineData("unregistered.gaomart.com.vn", HttpStatusCode.NotFound)]
    public async Task Unregistered_hosts_cannot_select_the_callback_target_even_with_its_key(string host, HttpStatusCode expected)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        await using var app = await RoutingTestHost(f);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Add("x-api-key", "test-key");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "www.gaomart.com.vn");
        using var result = await client.PostAsJsonAsync("/Admin/api-callback?tenant=doantl", Notification("unknown"));
        Assert.Equal(expected, result.StatusCode);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Empty_alias_configuration_keeps_existing_www_callback_store()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        await using var app = await RoutingTestHost(f, alias: null);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        client.DefaultRequestHeaders.Add("x-api-key", "other-store-key");
        using var result = await client.PostAsJsonAsync("/Admin/api-callback", Notification("unknown"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, (await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().SingleAsync()).StoreId);
    }
}

public sealed class AcbCallbackRoutingConfigurationTests
{
    [Theory]
    [InlineData("https://www.gaomart.com.vn")]
    [InlineData("*.gaomart.com.vn")]
    [InlineData("www.gaomart.com.vn:443")]
    [InlineData("www.gaomart.com.vn/")]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("www..gaomart.com.vn")]
    public void Invalid_callback_hosts_are_rejected(string host)
    {
        Assert.True(new AcbCallbackRoutingValidator().Validate(null, new AcbCallbackRoutingOptions { Hosts = [host] }).Failed);
    }

    [Fact]
    public void Host_matching_is_exact_case_insensitive_and_rejects_duplicate_configuration()
    {
        var options = new AcbCallbackRoutingOptions { Hosts = ["WWW.GAOMART.COM.VN", " www.gaomart.com.vn "] };
        Assert.True(new AcbCallbackRoutingValidator().Validate(null, options).Failed);
        options.Hosts.RemoveAt(1);
        Assert.True(new AcbCallbackRoutingValidator().Validate(null, options).Succeeded);
        Assert.True(options.Handles("www.gaomart.com.vn"));
        Assert.False(options.Handles("www.gaomart.com.vn.attacker.test"));
        Assert.False(options.Handles("gaomart.com.vn"));
    }

    [Fact]
    public void Application_registration_validates_bound_hosts_at_startup()
    {
        var services = new ServiceCollection();
        services.AddAcbCallbackRouting(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AcbCallbackRouting:Hosts:0"] = "*.gaomart.com.vn" }).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }
}

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private sealed class LookupAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Permission", out var permission)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "7"), new("permission", permission.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new(identity), Scheme.Name)));
        }
    }

    [Fact]
    public async Task Bank_lookup_HTTP_requires_permission_and_CSRF_and_returns_encoded_bank_data_without_callback_writes()
    {
        await using var f = await Fixture.Create();
        var response = LookupResponse("test-qr");
        response["responseBody"]!["orders"]![0]!["transactionDetail"]![0]!["transactionContent"] = "<script>bank-script</script>";
        var (service, bank) = await BankLookup(f, response);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing",
            ApplicationName = typeof(AcbReconciliationController).Assembly.FullName, ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(new AcbReconciliationService(f.Db));
        builder.Services.AddSingleton(f.Inbox);
        builder.Services.AddAuthentication("LookupTest").AddScheme<AuthenticationSchemeOptions, LookupAuthentication>("LookupTest", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(PermissionCodes.Pos.Payment.View,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permission", PermissionCodes.Pos.Payment.View)));
        builder.Services.Configure<RequestLocalizationOptions>(options => options.SetDefaultCulture("vi-VN")
            .AddSupportedCultures("vi-VN").AddSupportedUICultures("vi-VN"));
        await using var app = builder.Build();
        app.UseRequestLocalization(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapGet("/lookup-test-token", (IAntiforgery csrf, HttpContext context) => csrf.GetAndStoreTokens(context).RequestToken).RequireAuthorization();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        const string endpoint = "/admin/acb/reconciliation/bank-lookup";
        static FormUrlEncodedContent Fields(string from = "2026-10-12") => new(new Dictionary<string, string>
            { ["from"] = from, ["to"] = "2026-10-12", ["page"] = "1" });
        using (var anonymous = await client.PostAsync(endpoint, Fields())) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Permission", "denied");
        using (var denied = await client.PostAsync(endpoint, Fields())) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Permission");
        client.DefaultRequestHeaders.Add("X-Test-Permission", PermissionCodes.Pos.Payment.View);
        using (var noToken = await client.PostAsync(endpoint, Fields())) Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Empty(bank.Queries);
        var token = await client.GetStringAsync("/lookup-test-token");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        using (var success = await client.PostAsync(endpoint, Fields()))
        {
            var html = await success.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.True(success.Headers.CacheControl?.NoStore);
            Assert.Equal("bank-lookup", success.Headers.GetValues("X-Acb-Data-Source").Single());
            Assert.Contains("&lt;script&gt;bank-script", html);
            Assert.DoesNotContain("<script>bank-script", html);
            var decoded = WebUtility.HtmlDecode(html);
            Assert.Contains("Lấy từ ACB lúc 06:30:00 12/10/2026", decoded);
            Assert.Contains("Tạo QR: 00:01:00 12/10/2026", decoded);
            Assert.Contains("70.000", decoded);
            Assert.Contains("Chưa nhận callback", decoded);
            Assert.DoesNotContain("bank-private-sentinel", html);
            Assert.DoesNotContain("lookup-private-token", html);
            Assert.DoesNotContain("client-secret", html);
        }
        Assert.Empty(await f.Db.Set<GaoApp.Domain.Entities.AcbCallbackReceipt>().ToListAsync());
        Assert.Single(bank.Queries);
        using (var invalid = await client.PostAsync(endpoint, Fields("not-a-date"))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single(bank.Queries);
        bank.Status = HttpStatusCode.Forbidden; bank.RawBody = "bank-private-error-body";
        using (var failure = await client.PostAsync(endpoint, Fields()))
        {
            Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
            Assert.DoesNotContain("bank-private-error-body", await failure.Content.ReadAsStringAsync());
        }
        bank.Status = HttpStatusCode.OK; bank.RawBody = "bank-private-invalid-json";
        using (var failure = await client.PostAsync(endpoint, Fields()))
        {
            Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
            Assert.DoesNotContain("bank-private-invalid-json", await failure.Content.ReadAsStringAsync());
        }
        bank.RawBody = null;
        response["responseBody"]!["orders"]![0]!["transactionDetail"]![0]!["transactionContent"] = "Thanh toán đơn hàng tại Gạo Mart";
        using (var preview = await client.PostAsync(endpoint, Fields()))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var directory = Path.Combine(GaoApp.Tests.Security.FullApplicationFixture.SourceRoot(), "Logs", "acb-bank-lookup-20261012");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "bank-result.html"), await preview.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, f.FinalizeCount);
        await app.StopAsync();
    }
}

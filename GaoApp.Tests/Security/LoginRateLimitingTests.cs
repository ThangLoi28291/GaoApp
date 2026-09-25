using System.Net;
using GaoApp.Web.Areas.Admin.ViewModels.Account;
using GaoApp.Web.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class LoginRateLimitingTests
{
    [Fact]
    public async Task Fifty_accounts_share_one_ip_while_one_account_is_limited_and_csrf_still_required()
    {
        using var files = new HostFiles();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = files.Environment.ContentRootPath });
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLoginRateLimiting(builder.Configuration);
        builder.Services.AddAntiforgery();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var dbOptions = new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        builder.Services.AddScoped<AppDbContext>(_ => new InMemoryAppDbContext(dbOptions, new TenantContext(), new SessionPrincipalValidatorTests.CurrentUser()));
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LoginLimitProbeController).Assembly);
        await using var app = builder.Build();
        app.UseRouting(); app.UseRateLimiter(); app.MapControllers();
        app.MapGet("/csrf", (Microsoft.AspNetCore.Http.HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapPost("/ip-quota", () => "accepted").RequireRateLimiting("login");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var csrf = await client.GetStringAsync("/csrf");
            var statuses = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Send("user" + i, csrf)));
            Assert.All(statuses, x => Assert.Equal(HttpStatusCode.NoContent, x));
            // An invalid CSRF request cannot consume a target account's quota.
            for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.BadRequest, await Send("target", "invalid"));
            for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.NoContent, await Send(i % 2 == 0 ? "target" : " TARGET ", csrf));
            using var blocked = await client.PostAsync("/test/login-limit", Form("Target", csrf));
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            Assert.NotNull(blocked.Headers.RetryAfter);
            // IP quota is independent of the account quota: 64 attempts consumed, 56 left.
            for (var i = 0; i < 56; i++)
            {
                using var allowed = await client.PostAsync("/ip-quota", null);
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            }
            using var ipBlocked = await client.PostAsync("/ip-quota", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, ipBlocked.StatusCode);
            Assert.NotNull(ipBlocked.Headers.RetryAfter);

            async Task<HttpStatusCode> Send(string name, string token)
            {
                using var response = await client.PostAsync("/test/login-limit", Form(name, token));
                return response.StatusCode;
            }
        }
        finally { await app.StopAsync(); }
        static FormUrlEncodedContent Form(string name, string csrf) => new(new Dictionary<string, string>
            { ["UserName"] = name, ["Password"] = "test", ["__RequestVerificationToken"] = csrf });
    }

    [Fact]
    public void Account_quota_is_shared_across_requests_and_preserves_other_accounts()
    {
        using var limiter = new LoginAccountLimiter(Options.Create(new LoginRateLimitOptions()));
        Parallel.For(0, 10, _ => { using var lease = limiter.Acquire("account"); Assert.True(lease.IsAcquired); });
        using var blocked = limiter.Acquire(" ACCOUNT "); Assert.False(blocked.IsAcquired);
        using var other = limiter.Acquire("other"); Assert.True(other.IsAcquired);
        for (var i = 0; i < 10; i++) { using var lease = limiter.Acquire("name-alias-" + i, 7); Assert.True(lease.IsAcquired); }
        using var sameUser = limiter.Acquire("another-spelling", 7); Assert.False(sameUser.IsAcquired);
        using var differentUser = limiter.Acquire("name-alias-1", 8); Assert.True(differentUser.IsAcquired);
    }
}

public sealed class LoginLimitProbeController : Controller
{
    [HttpPost("/test/login-limit")]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    [ServiceFilter(typeof(LoginAccountRateLimitFilter))]
    public IActionResult Login(LoginVm vm) => NoContent();
}

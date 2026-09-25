using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class SensitiveEndpointHttpTests
{
    [Fact]
    public async Task Real_routes_reject_anonymous_and_underprivileged_requests_before_business_execution()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddScoped<ICurrentStorePermissionService, Permissions>();
        builder.Services.AddScoped<ITenantContext>(_ => { var t = new TenantContext(); t.SetStore(7, "store7"); return t; });
        builder.Services.AddScoped<AppDbContext>(sp => new InMemoryAppDbContext(
            new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            sp.GetRequiredService<ITenantContext>(), new SessionPrincipalValidatorTests.CurrentUser()));
        // Keep the real routing/authorization pipeline; short-circuit only business execution.
        builder.Services.AddControllersWithViews(o => o.Filters.Add(new NoBusinessExecution()))
            .AddApplicationPart(typeof(StoreBankAccountsController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var routes = new[] {
                ("/admin/api/audit-logs", "GET", new[] { PermissionCodes.System.AuditLog.View }),
                ("/admin/api/audit-logs/1", "GET", new[] { PermissionCodes.System.AuditLog.View }),
                ("/admin/audit-logs", "GET", new[] { PermissionCodes.System.AuditLog.View }),
                ("/admin/store-bank-accounts/data", "GET", new[] { PermissionCodes.System.BankAccount.View }),
                ("/Admin/StoreBankAccounts/Edit", "POST", new[] { PermissionCodes.System.BankAccount.View, PermissionCodes.System.BankAccount.Manage }),
                ("/Admin/StoreBankAccounts/SetDefault", "POST", new[] { PermissionCodes.System.BankAccount.View, PermissionCodes.System.BankAccount.Manage }),
                ("/Admin/StoreBankAccounts/ToggleStatus", "POST", new[] { PermissionCodes.System.BankAccount.View, PermissionCodes.System.BankAccount.Manage }),
                ("/admin/api/inventory-adjustments", "POST", new[] { PermissionCodes.Inventory.Adjustment.View, PermissionCodes.Inventory.Adjustment.Create, PermissionCodes.Inventory.Adjustment.Approve })
            };
            foreach (var (url, method, required) in routes)
            {
                var anonymousStatus = await Send(url, method, null);
                Assert.True(anonymousStatus == HttpStatusCode.Unauthorized, $"{method} {url}: anonymous returned {anonymousStatus}");
                Assert.Equal(HttpStatusCode.Forbidden, await Send(url, method, ""));
                foreach (var missing in required.Where(p => !(p == PermissionCodes.System.BankAccount.View && required.Contains(PermissionCodes.System.BankAccount.Manage))))
                    Assert.Equal(HttpStatusCode.Forbidden, await Send(url, method, string.Join(',', required.Except(new[] { missing }))));
                // A permitted POST still requires CSRF; permitted GET reaches the resource filter.
                Assert.Equal(method == "POST" ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent,
                    await Send(url, method, string.Join(',', required)));
            }

            async Task<HttpStatusCode> Send(string url, string method, string? permissions)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (permissions is not null)
                {
                    request.Headers.Add("X-Test-User", "1");
                    request.Headers.TryAddWithoutValidation("X-Test-Permissions", permissions);
                }
                using var response = await client.SendAsync(request);
                return response.StatusCode;
            }
        }
        finally { await app.StopAsync(); }
    }

    private sealed class NoBusinessExecution : IAsyncResourceFilter
    {
        public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            context.Result = new NoContentResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-User")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "test")));
        }
    }

    private sealed class Permissions(IHttpContextAccessor accessor) : ICurrentStorePermissionService
    {
        public Task<bool> HasPermissionAsync(int storeId, int userId, string permissionCode, CancellationToken ct = default)
            => Task.FromResult(storeId == 7 && userId == 1 && PermissionAliasMap.GetAcceptedCodes(permissionCode).Any(code => accessor.HttpContext!.User.HasClaim("permission", code)));
        public Task<List<string>> GetPermissionsAsync(int storeId, int userId, CancellationToken ct = default)
            => Task.FromResult(accessor.HttpContext!.User.FindAll("permission").Select(c => c.Value).ToList());
    }
}

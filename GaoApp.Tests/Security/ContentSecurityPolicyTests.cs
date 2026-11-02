using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Security;
using GaoApp.Web.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using GaoApp.Domain.Entities;
using GaoApp.Application.Common.Security;

namespace GaoApp.Tests.Security;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void Nonce_is_random_per_response_and_only_trusted_Razor_script_elements_receive_it()
    {
        var one = new DefaultHttpContext(); var two = new DefaultHttpContext();
        Assert.Equal(ContentSecurityPolicy.Nonce(one), ContentSecurityPolicy.Nonce(one));
        Assert.NotEqual(ContentSecurityPolicy.Nonce(one), ContentSecurityPolicy.Nonce(two));
        Assert.Equal(24, Convert.FromBase64String(ContentSecurityPolicy.Nonce(one)).Length);
        var output = new TagHelperOutput("script", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetContent("trusted();")));
        new ScriptNonceTagHelper { ViewContext = new ViewContext { HttpContext = one } }
            .Process(new TagHelperContext([], new Dictionary<object, object>(), "script"), output);
        Assert.Equal(ContentSecurityPolicy.Nonce(one), output.Attributes["nonce"].Value);
        var policy = ContentSecurityPolicy.Header(one);
        Assert.Contains("script-src 'nonce-", policy);
        Assert.Contains("script-src-attr 'none'", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.DoesNotContain("script-src 'self'", policy);
        Assert.DoesNotContain("strict-dynamic", policy);
    }

    [Fact]
    public void Every_vendored_asset_matches_the_reviewed_manifest_and_views_do_not_load_CDN_code()
    {
        var root = FullApplicationFixture.SourceRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng/security/browser-assets.manifest.json")));
        foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
        {
            var file = Path.Combine(root, "GaoApp.Web/wwwroot", asset.GetProperty("file").GetString()!);
            Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "GaoApp.Web"), "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var source = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(source, "<(?:script|link)\\b[^>]*(?:src|href)=\"https?://", RegexOptions.IgnoreCase), file);
            Assert.False(Regex.IsMatch(source, "\\bon(?:click|change|submit|load|error|input|keyup|keydown)=\"|href=\"javascript:", RegexOptions.IgnoreCase), file);
        }
    }
}

[Collection("R1FinalDatabasePreflight")]
public sealed class ContentSecurityPolicyBrowserTests
{
    [Fact]
    public async Task Actual_Razor_pages_work_with_CDNs_unavailable_and_enforced_CSP_blocks_untrusted_code()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[0]);
        // Monitor is deliberately opt-in, even for ADMIN. Grant it only in this owned test database.
        await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var roleId = await db.UserInStores.Where(x => x.UserId == account.UserId).Select(x => x.RoleId).SingleAsync();
            var permissionId = await db.Permissions.Where(x => x.Code == PermissionCodes.Admin.StoreMonitorView).Select(x => x.Id).SingleAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
            await db.SaveChangesAsync();
        }
        int receiptId;
        using (var cashier = await app.LoginAsync(account))
        {
            await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = account.Store.WarehouseId });
            var cart = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
            receiptId = cart.GetProperty("orderId").GetInt32();
            await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/{receiptId}/items?variantId={account.Store.VariantId}&qty=1", new { });
            await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", new { orderId = receiptId, clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
            await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/{receiptId}/invoice-route", new { route = 1 });
        }
        var root = FullApplicationFixture.SourceRoot();
        foreach (var reportOnly in new[] { true, false })
        {
            await app.ConfigureCspAsync(reportOnly);
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/security-csp.browser.cjs"));
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
                baseUrl = $"http://{account.Store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
                terminalId = account.Store.TerminalId, storeId = account.Store.StoreId, receiptId, reportOnly,
                output = Path.Combine(root, ".artifacts/security-issue12/browser", reportOnly ? "report-only" : "enforced") }));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr);
        }
    }
}

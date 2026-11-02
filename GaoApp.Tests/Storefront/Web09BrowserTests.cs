using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09"), Trait("Browser", "Chromium")]
public sealed class Web09BrowserTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_operator_activates_and_stops_Production_TLS_with_200percent_text_private_lost_response_replay_and_existing_guest_cancellation()
    {
        var c = await Web09Case.CreateAsync(fixture); var output = Web09Support.Runtime("WEB09 actual operator/public browser; emulation plus declared 200% user text, no real phone/UAT claim");
        await using var publicHost = await Web09PublicProcess.StartAsync(c); await using var adminHost = await Web09AdminHost.StartAsync(c);
        var chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe");
        Assert.True(File.Exists(chrome), "Use the already installed owned Chrome channel; no download or alternate browser fallback.");
        var input = new { publicBase = publicHost.BaseUrl, adminBase = adminHost.BaseUrl.TrimEnd('/'), productId = c.Seed.ProductId,
            userId = c.Seed.UserId, spkiPin = publicHost.SpkiPin, chromeExecutable = chrome, output };
        var run = BrowserAsync(input, output); var verified = false;
        try
        {
            while (!run.IsCompleted)
            {
                if (!verified && File.Exists(Path.Combine(output, "checkpoint-order-committed")))
                {
                    await using var db = c.Context(); var order = Assert.Single(await db.StorefrontOrders.AsNoTracking().ToListAsync());
                    Assert.Equal(45000m, order.TotalDue); Assert.Equal(StorefrontOrderPhase.AwaitingPreparation, order.Phase);
                    Assert.Equal(25000m, order.GoodsAmount); Assert.Equal(20000m, order.ShippingCharge);
                    Assert.Single(await db.Set<StorefrontCommandReceipt>().ToListAsync());
                    Assert.Equal(1m, (await db.InventoryReservations.SingleAsync()).ReservedQty);
                    Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
                    await File.WriteAllTextAsync(Path.Combine(output, "actual-commit-witness.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(),
                        c.StoreId, OrderId = order.Id, order.Code, order.GoodsAmount, order.ShippingCharge, order.TotalDue, GenuineSqlCommit = true, GuestCapabilityPersisted = false }));
                    verified = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-order-verified"), "Actual SQL: one45000COD order/onehold/oneplacementreceipt, no cash/POS/physical movement.");
                }
                await Task.WhenAny(run, Task.Delay(100));
            }
            using var report = await run; Assert.True(verified); Assert.True(report.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(4, report.RootElement.GetProperty("results").GetArrayLength());
            Assert.Equal(10, report.RootElement.GetProperty("textScale").GetArrayLength()); Assert.Equal(3, report.RootElement.GetProperty("skip").GetArrayLength());
            await AssertAssets(report.RootElement);
            await using var verify = c.Context(); var final = Assert.Single(await verify.StorefrontOrders.ToListAsync());
            Assert.Equal(StorefrontOrderPhase.Cancelled, final.Phase); Assert.Equal(45000m, final.TotalDue);
            Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled);
            Assert.Equal(InventoryReservationStatus.Released, (await verify.InventoryReservations.SingleAsync()).Status);
            Assert.Equal(1, await verify.Set<StorefrontCommandReceipt>().CountAsync(x => x.Operation == "place-cod-v1"));
            Assert.Empty(await verify.Orders.ToListAsync()); Assert.Empty(await verify.OrderPayments.ToListAsync()); Assert.Empty(await verify.InventoryTransactions.ToListAsync());
            Assert.Empty(await verify.AdminMenuItems.ToListAsync()); Assert.Empty(await verify.Set<StorefrontRefundEvidence>().ToListAsync());
        }
        finally
        {
            // Preserve actual effects even on a browser failure, before the
            // known fixture/hosts dispose; never convert the failure to PASS.
            await File.WriteAllTextAsync(Path.Combine(output, "actual-final-counts.json"), await c.Checkout.CountsAsync());
        }
    }
    private static async Task<JsonDocument> BrowserAsync(object input, string output)
    {
        var script = Path.Combine(Web09Support.Root(), "GaoApp.Tests.Browser/storefront-web09.browser.cjs");
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.GetDirectoryName(script)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }; start.ArgumentList.Add(script);
        using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        await File.WriteAllTextAsync(Path.Combine(output, "owned-node-process.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(),
            ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), Executable = process.MainModule?.FileName,
            Script = script, ScriptSHA256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), RuntimeRoot = output }));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input)); process.StandardInput.Close();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(9)); await process.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), await stderr);
            Assert.True(process.ExitCode == 0, "WEB09 actual browser failed; preserved evidence: " + output);
            return JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json")));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(output, "owned-node-ended.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
                ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), process.HasExited, process.ExitCode, NoUnknownProcessActions = true }));
        }
    }
    private static async Task AssertAssets(JsonElement report)
    {
        var rows = report.GetProperty("assets").EnumerateArray().ToArray(); Assert.NotEmpty(rows);
        Assert.Contains(rows, x => x.GetProperty("path").GetString() == "/assets/pending-order.js");
        Assert.Contains(rows, x => x.GetProperty("path").GetString() == "/lib/bootstrap/dist/css/bootstrap.min.css");
        foreach (var row in rows)
        {
            Assert.Equal("GET", row.GetProperty("method").GetString()); Assert.Equal(200, row.GetProperty("status").GetInt32());
            var origin = row.GetProperty("origin").GetString(); Assert.Contains(origin, new[] { "owned-public", "owned-admin" });
            var relative = row.GetProperty("path").GetString()!; Assert.DoesNotContain("..", relative); Assert.DoesNotContain('%', relative); Assert.DoesNotContain('\\', relative);
            if (origin == "owned-public") Assert.StartsWith("/assets/", relative, StringComparison.Ordinal);
            else Assert.Contains(relative, new[] { "/lib/bootstrap/dist/css/bootstrap.min.css", "/css/site.css" });
            var root = Path.GetFullPath(Path.Combine(Web09Support.Root(), origin == "owned-public" ? "GaoApp.Storefront.Web/wwwroot" : "GaoApp.Web/wwwroot"));
            var file = Path.GetFullPath(Path.Combine(root, relative.TrimStart('/'))); Assert.StartsWith(root + Path.DirectorySeparatorChar, file, StringComparison.OrdinalIgnoreCase);
            Web09Support.NoReparse(file); Assert.True(File.Exists(file)); await using var stream = File.OpenRead(file);
            Assert.Equal(Convert.ToHexString(await SHA256.HashDataAsync(stream)), row.GetProperty("sha256").GetString());
        }
    }
}

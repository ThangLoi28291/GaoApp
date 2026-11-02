using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04"), Trait("Browser", "Chromium")]
public sealed class Web04BrowserTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_mobile_COD_browser_requotes_changed_price_recovers_lost_placement_response_tracks_private_snapshot_and_measures_responsive_UI()
    {
        var c = await fixture.Web04Async(); var second = await c.Seed.AddProductAsync("WEB04 browser fractional second");
        await using var host = await Web04PublicProcess.StartAsync(c); var output = Web04Support.Artifact("browser/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var info = new { host.BaseUrl, output, primary = new { c.Seed.ProductId, c.Seed.VariantId, c.Seed.UnitId }, secondary = second,
            originalGoods = "5 đ", originalTotal = "20.005 đ", changedGoods = "7 đ", changedTotal = "20.007 đ", preciseUnit = "9.007.199.254.740.993 đ", preciseTotal = "9.007.199.254.760.993 đ" };
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web04.browser.cjs")); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new { process.Id, Started = DateTime.UtcNow }));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.StandardInput.WriteAsync(JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web))); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var exit = process.WaitForExitAsync(timeout.Token); var price = false; var requote = false; var lost = false; var precision = false;
        try
        {
            while (!exit.IsCompleted)
            {
                if (!price && File.Exists(Path.Combine(output, "checkpoint-price-ready")))
                { await Web04PromotionTests.SetBasePrice(c, 45000); price = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-price-mutated"), "Fixture conversion price changed to45000; no production data."); }
                if (!requote && File.Exists(Path.Combine(output, "checkpoint-requote-ready")))
                {
                    await using var verify = c.Context(); Assert.Empty(await verify.StorefrontOrders.ToListAsync()); Assert.Empty(await verify.InventoryReservations.ToListAsync());
                    requote = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-requote-verified"), "No order, hold or placement receipt before reconfirmation.");
                }
                if (!lost && File.Exists(Path.Combine(output, "checkpoint-lost-ready")))
                {
                    await using var verify = c.Context(); Assert.Single(await verify.StorefrontOrders.ToListAsync()); Assert.Equal(2, await verify.InventoryReservations.CountAsync());
                    Assert.Single(await verify.Set<GaoApp.Domain.Storefront.StorefrontCommandReceipt>().ToListAsync()); lost = true;
                    await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-lost-verified"), "Committed exactly once although browser did not receive response.");
                }
                if (!precision && File.Exists(Path.Combine(output, "checkpoint-precision-ready")))
                { await Web04PromotionTests.SetBasePrice(c, 9007199254740993m); precision = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-precision-mutated"), "Fixture price updated; received order snapshot retained."); }
                await Task.WhenAny(exit, Task.Delay(100, timeout.Token));
            }
            await exit; var error = await stderr; await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
            Assert.True(process.ExitCode == 0, "WEB04 actual browser failed: " + error + " Evidence: " + output); Assert.True(price && requote && lost && precision);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json"))); Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), row => Assert.True(row.GetProperty("passed").GetBoolean()));
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync(); Assert.Equal(7, order.GoodsAmount); Assert.Equal(20000, order.ShippingCharge); Assert.Equal(20007, order.TotalDue);
        var lines = await db.Set<GaoApp.Domain.Storefront.StorefrontOrderLine>().ToListAsync(); Assert.Equal(2, lines.Count); Assert.All(lines, x => Assert.Equal(.0001m, x.Quantity)); Assert.Equal(-1, lines.Sum(x => x.RoundingAdjustment));
        Assert.Equal(.0002m, await db.InventoryReservations.SumAsync(x => x.ReservedQty)); Assert.Equal(100, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.Seed.VariantId)).OnHandQty);
        Assert.Single(await db.Set<GaoApp.Domain.Storefront.StorefrontEvent>().ToListAsync()); Assert.Single(await db.Set<GaoApp.Domain.Storefront.StorefrontOutboxMessage>().ToListAsync());
        Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }
}

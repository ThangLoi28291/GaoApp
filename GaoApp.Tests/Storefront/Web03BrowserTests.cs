using System.Diagnostics;
using System.Text.Json;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb03"), Trait("Category", "StorefrontWeb03"), Trait("Browser", "Chromium")]
public sealed class Web03BrowserTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_customer_browser_flow_responsive_cart_price_visibility_and_same_origin_store_scope()
    {
        var c = await fixture.CaseAsync(); var otherStore = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync(); await otherStore.EnableSyntheticCatalogAsync();
        var name = "Bình giữ nhiệt WEB03 — " + string.Join(" ", Enumerable.Repeat("tiện dụng cho căn bếp gia đình", 5)); Assert.True(name.Length <= 200);
        int alternativeId;
        await using (var db = c.Context())
        {
            var first = await db.ProductVariants.SingleAsync(x => x.Id == c.VariantId); first.ProductVariantName = "Màu xanh — bán theo thùng";
            var alternative = new ProductVariant { StoreId = c.StoreId, ProductId = c.ProductId, Sku = "W03-ALT-" + Guid.NewGuid().ToString("N"), ProductVariantName = "Màu trắng — bán theo đơn vị gốc", Price = 25000, IsActive = true };
            db.ProductVariants.Add(alternative); await db.SaveChangesAsync(); alternativeId = alternative.Id;
            db.ProductUnitConversions.Add(new() { StoreId = c.StoreId, ProductVariantId = alternativeId, UnitId = c.UnitId, Factor = 1, Price = 25000, IsBaseUnit = true });
            db.InventoryBalances.Add(new() { StoreId = c.StoreId, WarehouseId = c.WarehouseId, ProductVariantId = c.VariantId, OnHandQty = -3, ReservedQty = 2 }); await db.SaveChangesAsync();
        }
        var asset = await c.AssetAsync("uploads/products/web03-browser.png", "image/png"); var uploads = await c.UploadsAsync((asset, "uploads/products/web03-browser.png", Web03Support.Png));
        await using (var db = c.Context())
        {
            var request = c.Publication(baseUnit: false, pack: pack.Conversion); request.DisplayName = name; request.SortOrder = 0;
            request.Media = [new() { MediaAssetId = asset, Kind = GaoApp.Domain.Storefront.StorefrontMediaKind.Image, AltText = name }];
            request.Variants.Add(new() { VariantId = alternativeId, IsPublished = true, BaseUnitIsPublished = true, BaseUnitWeightKg = 0.25m });
            await c.Products(db).SaveAsync(c.ProductId, request);
        }
        var secondary = await c.AddProductAsync("Đèn học bàn WEB03", "Điện gia dụng", 1);
        var rounding = new[] { await c.AddProductAsync("WEB03 rounding A", order: 20), await c.AddProductAsync("WEB03 rounding B", order: 21) };
        var precision = await c.AddProductAsync("WEB03 exact large money", order: 200, price: 9007199254740993m);
        for (var i = 0; i < 26; i++) await c.AddProductAsync("WEB03 Z" + i.ToString("D2"), "Nhóm tổng hợp", 100 + i);
        var primary = new Web03Product(c.ProductId, c.VariantId, pack.Unit, name); var alternativeChoice = new Web03Product(c.ProductId, alternativeId, c.UnitId, name);
        var before = await c.SideEffectsAsync(); var run = "browser/" + Guid.NewGuid().ToString("N"); var state = Web03Support.Artifact(run + "/cart-storage-state.json");
        int port; string previousScope, previousKey;
        await using (var host = await Web03PublicProcess.StartAsync(c, uploads))
        {
            port = new Uri(host.BaseUrl).Port;
            await Browser("initial", host.BaseUrl, "initial");
            using (var scope = JsonDocument.Parse(await File.ReadAllTextAsync(Web03Support.Artifact(run + "/initial/cart-scope.json"))))
            { previousScope = scope.RootElement.GetProperty("scope").GetString()!; previousKey = scope.RootElement.GetProperty("key").GetString()!; }
            await using (var db = c.Context())
            {
                await db.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 190000));
                await db.StorefrontProducts.Where(x => x.ProductId == secondary.ProductId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false));
            }
            await Browser("revalidate", host.BaseUrl, "revalidate");
        }
        await using (var mapped = await Web03PublicProcess.StartAsync(otherStore, uploads, fixedPort: port))
            await Browser("scope", mapped.BaseUrl, "scope", previousScope, previousKey);
        Assert.Equal(before, await c.SideEffectsAsync());

        async Task Browser(string phase, string baseUrl, string directory, string? oldScope = null, string? oldKey = null)
        {
            var output = Web03Support.Artifact(run + "/" + directory); Directory.CreateDirectory(output);
            var info = new { phase, baseUrl, output, storageState = state, primary, secondary, alternative = alternativeChoice, rounding, precision, previousScope = oldScope, previousKey = oldKey };
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web03.browser.cjs"));
            using var process = new Process { StartInfo = start }; Assert.True(process.Start());
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web))); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw new TimeoutException("Owned WEB03 browser phase timed out: " + phase); }
            var error = await stderr; await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
            Assert.True(process.ExitCode == 0, "Actual browser phase " + phase + " failed: " + error + " Evidence: " + output);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json")));
            Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), row => Assert.True(row.GetProperty("passed").GetBoolean()));
        }
    }
}

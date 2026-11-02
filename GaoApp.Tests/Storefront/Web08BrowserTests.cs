using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb08"), Trait("Category", "StorefrontWeb08"), Trait("Browser", "Chromium")]
public sealed class Web08BrowserTests(Web08Fixture fixture)
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_lazy_3D_lifecycle_keyboard_touch_responsive_and_reduced_motion_preserve_exact_pack_COD_retry_and_private_tracking(bool reduced)
    {
        var c = await Web08Case.CreateAsync(fixture); var output = Web08Support.NewDirectory("WEB08 actual public/browser lifecycle and exact business invariants");
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        var run = Web08Node.RunAsync(Input(c, host, output, "lifecycle", reduced), output, "Real separate public host, native WebGL and actual COD transaction", TimeSpan.FromMinutes(9));
        var checkedCommit = false;
        while (!run.IsCompleted)
        {
            if (!checkedCommit && File.Exists(Path.Combine(output, "checkpoint-placement-committed")))
            {
                await AssertReceivedAsync(c); checkedCommit = true;
                await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-placement-verified"), "Actual SQL committed one order/hold/receipt; no capability.");
            }
            await Task.WhenAny(run, Task.Delay(100));
        }
        using var report = await run; Assert.True(checkedCommit); Assert.Equal(6, report.RootElement.GetProperty("results").GetArrayLength());
        await AssertReceivedAsync(c); await AssertServedAssetsAsync(report);
    }

    [Theory]
    [InlineData("no-model")][InlineData("no-webgl")][InlineData("module")][InlineData("model")]
    [InlineData("offline")][InlineData("timeout")][InlineData("late-close")][InlineData("late-swap")][InlineData("font")]
    public async Task Actual_optional_viewer_failure_or_late_work_keeps_image_price_and_cart_usable_without_orders_or_inventory_effects(string failure)
    {
        var c = await Web08Case.CreateAsync(fixture, models: failure != "no-model"); var before = await c.Web.CountsAsync();
        var output = Web08Support.NewDirectory("WEB08 controlled viewer fault " + failure + "; no fabricated business response");
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        using var report = await Web08Node.RunAsync(new { mode = "failure", failure, baseUrl = host.BaseUrl, output,
            productId = c.ProductId, primaryVariant = c.Seed.VariantId, baseUnit = c.Seed.UnitId, glbId = c.GlbId, gltfId = c.GltfId }, output, "Failure injection explicitly separated from real asset success");
        Assert.Equal(before, await c.Web.CountsAsync()); await AssertServedAssetsAsync(report);
    }

    [Fact]
    public async Task Controlled_early_outer_parser_error_drains_real_native_image_sibling_before_explicit_retry_and_preserves_purchase_focus()
    {
        var original = await Web08Case.CreateAsync(fixture); var id = await original.AssetAsync("two-native-images.gltf", "model/gltf+json", Web08FixtureAssets.TwoImageModel());
        await using (var db = original.Context())
        {
            var publication = await original.Seed.SavedAsync(); publication.Media.Single(x => x.MediaAssetId == original.GlbId).MediaAssetId = id;
            await original.Seed.Products(db).SaveAsync(original.ProductId, publication);
        }
        var c = original with { GlbId = id }; var before = await c.Web.CountsAsync(); var output = Web08Support.NewDirectory("WEB08 native dependency failure/drain recovery; actual small decoded images, no fake positive model");
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        using var report = await Web08Node.RunAsync(new { mode = "dependencies", baseUrl = host.BaseUrl, output,
            productId = c.ProductId, primaryVariant = c.Seed.VariantId, baseUnit = c.Seed.UnitId, glbId = id }, output,
            "Controlled EARLY outer parser callback error while actual native results remain pending; explicit same-page retry");
        Assert.Equal(before, await c.Web.CountsAsync()); await AssertServedAssetsAsync(report);
    }

    [Fact]
    public async Task Actual_home_catalog_empty_error_loading_and_add_cart_feedback_have_accessible_nonblocking_mobile_hierarchy()
    {
        var c = await Web08Case.CreateAsync(fixture); var before = await c.Web.CountsAsync();
        var output = Web08Support.NewDirectory("WEB08 actual home/catalog visual states and controlled latency/error feedback");
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        using var report = await Web08Node.RunAsync(new { mode = "experience", baseUrl = host.BaseUrl, output,
            productId = c.ProductId, primaryVariant = c.Seed.VariantId, baseUnit = c.Seed.UnitId, imageId = c.ImageId }, output, "Real public Razor and actual cart service; controlled latency/fault only");
        Assert.Equal(before, await c.Web.CountsAsync()); await AssertServedAssetsAsync(report);
    }

    [Theory]
    [InlineData("mobile")][InlineData("desktop")]
    public async Task All_declared_lab_runs_keep_actual_LCP_EventTiming_CLS_and_current_asset_proofs_without_fastest_run_selection(string profile)
    {
        var c = await Web08Case.CreateAsync(fixture); var before = await c.Web.CountsAsync(); var output = Web08Support.NewDirectory("WEB08 declared lab profile " + profile + "; emulation only, no physical phone claim");
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        using var report = await Web08Node.RunAsync(new { mode = "performance", profile, baseUrl = host.BaseUrl, output,
            productId = c.ProductId, primaryVariant = c.Seed.VariantId, alternateVariant = c.AlternateVariantId, baseUnit = c.Seed.UnitId }, output,
            "Declared BEFORE-execution lab profile, all raw runs", TimeSpan.FromMinutes(12));
        Assert.Equal(profile == "mobile" ? 6 : 3, report.RootElement.GetProperty("performance").GetArrayLength());
        Assert.Equal(before, await c.Web.CountsAsync()); await AssertServedAssetsAsync(report);
    }

    [Fact]
    public async Task Poisoned_model_transport_is_rejected_by_client_preflight_before_verified_parser_tripwire_while_actual_server_denies_same_witness()
    {
        var c = await Web08Case.CreateAsync(fixture); var witness = Web08FixtureAssets.OverlappingViewAllocationWitness();
        var id = await c.AssetAsync("client-preflight-witness.glb", "model/gltf-binary", witness);
        await using (var db = c.Context())
        {
            var request = await c.Seed.SavedAsync(); request.Media.Add(new() { MediaAssetId = id, Kind = StorefrontMediaKind.Model3D, AltText = "Unsafe preflight witness; not merchant content" });
            await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        }
        var output = Web08Support.NewDirectory("WEB08 explicit poisoned media transport, fail-fast parser tripwire, never actual unsafe parse");
        var payload = Path.Combine(output, "bounded-transport-fault.glb"); await File.WriteAllBytesAsync(payload, witness);
        await using var host = await Web04PublicProcess.StartAsync(c.Web, uploads: c.Uploads);
        using (var actual = await host.Http.GetAsync("/shop/media/" + id)) Assert.Equal(System.Net.HttpStatusCode.NotFound, actual.StatusCode);
        var before = await c.Web.CountsAsync();
        using var report = await Web08Node.RunAsync(new { mode = "client-preflight", baseUrl = host.BaseUrl, output,
            productId = c.ProductId, primaryVariant = c.Seed.VariantId, baseUnit = c.Seed.UnitId, glbId = c.GlbId, payload,
            payloadSHA256 = Convert.ToHexString(SHA256.HashData(witness)), transportFaultInjected = true }, output, "Controlled media-only fault; actual server404 proven separately");
        Assert.True(report.RootElement.GetProperty("transportFaultInjected").GetBoolean()); Assert.Equal(0, report.RootElement.GetProperty("parserEntries").GetInt32());
        Assert.Equal(before, await c.Web.CountsAsync()); await AssertServedAssetsAsync(report);
    }

    private static object Input(Web08Case c, Web04PublicProcess host, string output, string mode, bool reduced) => new
    {
        mode, reduced, baseUrl = host.BaseUrl, output, productId = c.ProductId, primaryVariant = c.Seed.VariantId,
        alternateVariant = c.AlternateVariantId, baseUnit = c.Seed.UnitId, packUnit = c.Web.PackUnit, glbId = c.GlbId, gltfId = c.GltfId
    };

    private static async Task AssertReceivedAsync(Web08Case c)
    {
        await using var db = c.Context(); var parent = Assert.Single(await db.StorefrontOrders.ToListAsync());
        Assert.Equal(275000m, parent.GoodsAmount); Assert.Equal(20000m, parent.ShippingCharge); Assert.Equal(295000m, parent.TotalDue);
        var line = Assert.Single(await db.Set<StorefrontOrderLine>().ToListAsync()); Assert.Equal(c.Seed.VariantId, line.VariantId); Assert.Equal(c.Web.PackUnit, line.SellingUnitId);
        Assert.Equal(1m, line.Quantity); Assert.Equal(12m, line.BaseQuantity);
        var hold = Assert.Single(await db.InventoryReservations.ToListAsync()); Assert.Equal(12m, hold.ReservedQty);
        Assert.Single(await db.Set<StorefrontCommandReceipt>().ToListAsync()); Assert.Single(await db.Set<StorefrontEvent>().ToListAsync()); Assert.Single(await db.Set<StorefrontOutboxMessage>().ToListAsync());
        var stock = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.Seed.VariantId); Assert.Equal(100m, stock.OnHandQty); Assert.Equal(12m, stock.ReservedQty);
        Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }

    private static async Task AssertServedAssetsAsync(JsonDocument report)
    {
        var webRoot = Path.GetFullPath(Path.Combine(Web08Support.Root(), "GaoApp.Storefront.Web", "wwwroot"));
        foreach (var asset in report.RootElement.GetProperty("assets").EnumerateArray())
        {
            Assert.False(asset.TryGetProperty("captureError", out _), asset.ToString());
            var route = asset.GetProperty("path").GetString()!; Assert.StartsWith("/assets/", route); Assert.DoesNotContain("..", route);
            var file = Path.GetFullPath(Path.Combine(webRoot, route.TrimStart('/')));
            Assert.StartsWith(webRoot + Path.DirectorySeparatorChar, file, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(file), file); Web08Support.NoReparse(file);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))), asset.GetProperty("sha256").GetString());
            Assert.Equal(200, asset.GetProperty("status").GetInt32()); Assert.Equal("GET", asset.GetProperty("method").GetString());
        }
    }
}

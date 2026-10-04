using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingAllocationUiContractTests
{
    [Fact]
    public async Task Actual_browser_prices_discounts_and_same_sku_gifts_apply_without_posting()
    {
        await using var app = await GaoApp.Tests.Security.FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        var account = await app.AddAccountAsync(store, "*");
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        if (!await db.ProductUnitConversions.AnyAsync(x => x.ProductVariantId == variant.Id && x.UnitId == variant.Product.BaseUnitId))
            db.ProductUnitConversions.Add(new() { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = variant.Product.BaseUnitId, Factor = 1m, IsBaseUnit = true });
        var image = new GaoApp.Domain.Entities.ProductImage
        {
            StoreId = store.StoreId, ProductId = variant.ProductId, IsPrimary = true,
            MediaAsset = new() { StoreId = store.StoreId, StoragePath = "img/avatars/1.png", ContentType = "image/png" }
        };
        db.Add(image); await db.SaveChangesAsync(); variant.PrimaryProductImageId = image.Id; await db.SaveChangesAsync();
        var lines = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.StockDocumentLines.Where(x => x.StockDocumentId == seed.ReceiptId));
        var root = Directory.GetCurrentDirectory(); while (!File.Exists(Path.Combine(root, "GaoApp.sln"))) root = Directory.GetParent(root)!.FullName;
        var evidence = Environment.GetEnvironmentVariable("GAOAPP_PRICING_EVIDENCE_ROOT") ?? Path.Combine(Path.GetTempPath(), "GaoAppPricingBrowser_" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/receipt-pricing-allocation.browser.cjs"));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
        { baseUrl = $"http://{store.Host}:{app.Address.Port}", user = account.Name, password = account.Password, terminalId = store.TerminalId,
            receiptId = seed.ReceiptId, imageUrl = "/img/avatars/1.png", lines = lines.Select(x => new { id = x.Id, unitId = x.UnitId, factor = x.Factor }), evidenceRoot = evidence }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.PurchasePayables));
        Assert.Equal(355m, (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.StockDocuments, x => x.Id == seed.ReceiptId)).SubtotalBeforeVat);
    }

    [Fact]
    public void Workbench_contains_bill_reconciliation_rule_editor_and_separate_apply_with_safe_server_results()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "GaoApp.sln"))) root = Directory.GetParent(root)!.FullName;
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js"));
        foreach (var contract in new[] { "data-workbench-tab=\"pricing\"", "pricingAllocationLines", "pricingActualBillTotal", "pricingSystemTotal", "pricingDifference", "pricingRuleList", "pricingApply", "pricingBillSearch", "pricingRuleEditor", "pricingQuantityComparison", "commercialApproveButton" }) Assert.Contains(contract, view);
        Assert.DoesNotContain("readonly=\"@(readOnly || allocationApplied)\"", view);
        Assert.Contains("preview = await api('POST', '/preview', draft)", script);
        Assert.Contains("receiptRowVersion: workspace.receiptRowVersion, planRowVersion: workspace.planRowVersion", script);
        Assert.Contains("response.status === 409", script);
        Assert.Contains("workspace.isStale", script);
        Assert.Contains("el.textContent", script);
        Assert.Contains("finalAmountBeforeVat", script);
    }
}

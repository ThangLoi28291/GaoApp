using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Purchases;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptQuickPriceSqlServerTests
{
    [Fact]
    public async Task Quick_price_is_local_until_confirmed_then_saves_only_the_selected_goods_price_without_posting_or_creating_a_plan()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        var account = await app.AddAccountAsync(store, "*");
        int setLineId, packLineId, cartonLineId, baseUnitId, setUnitId, packUnitId, cartonUnitId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            var variant = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit).SingleAsync(x => x.Id == store.VariantId);
            variant.ProductVariantName = variant.Product.Name = "Đèn thờ sen";
            variant.Product.BaseUnit.Name = "Cái";
            baseUnitId = variant.Product.BaseUnitId;
            if (!await db.ProductUnitConversions.AnyAsync(x => x.ProductVariantId == variant.Id && x.UnitId == baseUnitId && x.Factor == 1m))
                db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                    UnitId = baseUnitId, Factor = 1m, IsBaseUnit = true, IsActive = true });
            var set = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                Unit = new Unit { StoreId = store.StoreId, Code = "QUICK-SET", Name = "Set", IsActive = true }, Factor = 2m, IsActive = true };
            db.Add(set); await db.SaveChangesAsync();
            setUnitId = set.UnitId;
            var pack = receipt.Lines.Single(x => x.ProductUnitConversionId == seed.PackId);
            var carton = receipt.Lines.Single(x => x.ProductUnitConversionId == seed.CartonId);
            packLineId = pack.Id; cartonLineId = carton.Id;
            packUnitId = pack.UnitId ?? throw new InvalidOperationException("Pack fixture requires a unit.");
            cartonUnitId = carton.UnitId ?? throw new InvalidOperationException("Carton fixture requires a unit.");
            pack.Quantity = 3m; pack.BaseQuantity = 12m; pack.LineTotal = 30m;
            foreach (var line in receipt.Lines) line.ProductNameSnapshot = variant.ProductVariantName;
            var setLine = new StockDocumentLine { StockDocumentId = receipt.Id, ProductVariantId = variant.Id,
                ProductUnitConversionId = set.Id, UnitId = set.UnitId, UnitNameSnapshot = "Set", Factor = 2m,
                Quantity = 20m, BaseQuantity = 40m, LineNo = 3, UnitPriceBeforeVat = 10m, UnitPriceAfterVat = 10m,
                UnitCost = 10m, LineTotal = 200m, ProductNameSnapshot = variant.ProductVariantName };
            db.Add(setLine); await db.SaveChangesAsync(); setLineId = setLine.Id;
            receipt.SubtotalBeforeVat = receipt.TotalAmount = receipt.Lines.Sum(x => x.LineTotal);
            await db.SaveChangesAsync();
        }
        var before = await PhysicalSnapshotAsync(app, store, seed.ReceiptId);
        await RunBrowserAsync(app, account, new { receiptId = seed.ReceiptId, setLineId, packLineId, cartonLineId,
            baseUnitId, setUnitId, packUnitId, cartonUnitId });
        Assert.Equal(before, await PhysicalSnapshotAsync(app, store, seed.ReceiptId));
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(79000m, (await check.StockDocumentLines.SingleAsync(x => x.Id == setLineId)).UnitPriceBeforeVat);
        Assert.Equal(33333.33m, (await check.StockDocumentLines.SingleAsync(x => x.Id == packLineId)).UnitPriceBeforeVat);
        Assert.Equal(10m, (await check.StockDocumentLines.SingleAsync(x => x.Id == cartonLineId)).UnitPriceBeforeVat);
        Assert.Empty(await check.PurchaseReceiptPricingPlans.ToListAsync());
        Assert.Equal(StockDocumentStatus.PendingApproval, (await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status);

        // Read-only receipts must not expose a second path to edit prices.
        var confirmed = await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
        confirmed.Status = StockDocumentStatus.Confirmed;
        confirmed.ConfirmedLegalEntityId = await check.Warehouses.Where(x => x.Id == confirmed.WarehouseId)
            .Select(x => x.LegalEntityId).SingleAsync();
        await check.SaveChangesAsync();
        using var manager = await app.LoginAsync(account);
        var html = await manager.Http.GetStringAsync($"/admin/stock-documents/{seed.ReceiptId}");
        Assert.DoesNotContain("data-qprice-open", html, StringComparison.Ordinal);
    }

    private static async Task<string> PhysicalSnapshotAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new
        {
            Receipt = await db.StockDocuments.AsNoTracking().Where(x => x.Id == receiptId)
                .Select(x => new { x.Status, x.SupplierId, x.WarehouseId, x.DocumentDate, x.ApprovedAtUtc, x.ConfirmedAtUtc }).SingleAsync(),
            Lines = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == receiptId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ProductVariantId, x.ProductUnitConversionId, x.UnitId, x.Factor, x.Quantity, x.BaseQuantity }).ToListAsync(),
            Inventory = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost }).ToListAsync(),
            Balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty }).ToListAsync(),
            Payables = await db.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync(),
            VariantCosts = await db.ProductVariants.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CostPrice }).ToListAsync(),
            LineMapCount = await db.StockDocumentLineInputInvoiceMaps.CountAsync(),
            PricingPlanCount = await db.PurchaseReceiptPricingPlans.CountAsync()
        });
    }

    private static async Task RunBrowserAsync(FullApplicationFixture app, FullApplicationFixture.Account account, object seed)
    {
        var root = FullApplicationFixture.SourceRoot();
        var evidence = Environment.GetEnvironmentVariable("GAOAPP_QUICK_PRICE_EVIDENCE_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "GaoAppQuickPriceBrowser_" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "receipt-quick-price.browser.cjs"));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{account.Store.Host}:{app.Address.Port}",
            user = account.Name, password = account.Password, terminalId = account.Store.TerminalId, seed, evidenceRoot = evidence }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}

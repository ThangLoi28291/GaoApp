using System.Net.Http.Json;
using System.Net;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingAllocationConfirmTransactionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Confirm_uses_saved_goods_prices_preserves_untouched_exact_amounts_and_rolls_back_posting_and_plan_together(bool manualOverride, bool hasVat)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        int? taxId = null;
        if (hasVat)
        {
            var tax = new GaoApp.Domain.Entities.Tax { StoreId = store.StoreId, Code = "MANUAL-VAT", Name = "VAT 10%", Rate = 10m };
            db.Add(tax); await db.SaveChangesAsync(); taxId = tax.Id;
            var draftReceipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            draftReceipt.HasVat = true; draftReceipt.IncludeVatInInventoryCost = true;
            foreach (var line in draftReceipt.Lines) { line.TaxId = tax.Id; line.TaxRate = tax.Rate; }
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url,
            PricingAllocationTestData.Request(await PricingAllocationTestData.Get(admin, url), .505m)));
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion }));
        var receipt = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        var transactions = await db.InventoryTransactions.CountAsync();
        var current = applied;
        var editedId = receipt.Lines.OrderBy(x => x.LineNo).First().Id;
        var subtotal = manualOverride ? 247.01m : 2.02m;
        var total = hasVat ? 271.71m : subtotal;
        if (manualOverride)
        {
            var draftPayload = new { rowVersion = applied.ReceiptRowVersion,
                lines = receipt.Lines.Select(x => new { stockDocumentLineId = x.Id,
                    rowVersion = Convert.ToBase64String(x.RowVersion), unitPriceBeforeVat = x.Id == editedId ? 123m : .51m }).ToArray() };
            var result = await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/price-draft", draftPayload);
            Assert.Equal(1.01m, result.GetProperty("lineAmountsBeforeVat").GetProperty(receipt.Lines.Single(x => x.Id != editedId).Id.ToString()).GetDecimal());
            using var stale = await admin.Http.PostAsJsonAsync($"/admin/api/stock-documents/{seed.ReceiptId}/price-draft", draftPayload);
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
            current = await PricingAllocationTestData.Get(admin, url);
            Assert.True(current.IsStale);
            Assert.Equal(123m, current.PhysicalLines.Single(x => x.StockDocumentLineId == editedId).CurrentUnitPriceBeforeVat);
            Assert.Equal(246m, current.PhysicalLines.Single(x => x.StockDocumentLineId == editedId).CurrentAmountBeforeVat);
            // A revised, unapplied helper must not replace or block final Goods prices.
            current = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, PricingAllocationTestData.Request(current, .6m)));
            Assert.Equal(PurchaseReceiptPricingPlanState.Draft, current.State);
            Assert.Equal(total, (await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == seed.ReceiptId)).TotalAmount);
            Assert.Equal(transactions, await db.InventoryTransactions.CountAsync());
            Assert.False(await db.PurchasePayables.AnyAsync());
        }
        // Disposable fixture only: fail the final plan UPDATE after posting has been staged in the same transaction.
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [dbo].[TestPricingConfirmRollback] ON [dbo].[PurchaseReceiptPricingPlan] AFTER UPDATE AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [State] = 3) THROW 51001, 'Injected test commit failure', 1; END;");
        var payload = new { rowVersion = current.ReceiptRowVersion, supplierId = receipt.SupplierId,
            hasVat, includeVatInInventoryCost = hasVat,
            isMerchandisePaid = false, acceptPriceVariance = true,
            lines = receipt.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = 999999m, taxId }).ToArray() };
        using (var failed = await admin.Http.PostAsJsonAsync($"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", payload)) Assert.False(failed.IsSuccessStatusCode);
        Assert.Equal(current.State, (await db.PurchaseReceiptPricingPlans.AsNoTracking().SingleAsync()).State);
        Assert.Equal(StockDocumentStatus.PendingApproval, (await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        Assert.Equal(transactions, await db.InventoryTransactions.CountAsync()); Assert.False(await db.PurchasePayables.AnyAsync());
        Assert.Equal(100m, (await db.InventoryBalances.AsNoTracking().SingleAsync()).OnHandQty);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [dbo].[TestPricingConfirmRollback];");
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", payload);
        Assert.Equal(PurchaseReceiptPricingPlanState.Confirmed, (await db.PurchaseReceiptPricingPlans.AsNoTracking().SingleAsync()).State);
        var confirmed = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Confirmed, confirmed.Status); Assert.Equal(total, confirmed.TotalAmount);
        Assert.Equal(subtotal, confirmed.SubtotalBeforeVat);
        Assert.All(confirmed.Lines, x => Assert.Equal(manualOverride && x.Id == editedId ? (hasVat ? 270.6m : 246m) : (hasVat ? 1.11m : 1.01m), x.LineTotal));
        Assert.Equal(total, (await db.PurchasePayables.AsNoTracking().SingleAsync()).Amount);
        var movements = await db.InventoryTransactions.AsNoTracking().Where(x => x.ReferenceId == seed.ReceiptId.ToString()).ToArrayAsync();
        Assert.Equal(total, decimal.Round(movements.Sum(x => x.TotalCost), 2, MidpointRounding.AwayFromZero));
        Assert.Equal(156m, (await db.InventoryBalances.AsNoTracking().SingleAsync()).OnHandQty);
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", payload);
        Assert.Equal(transactions + 2, await db.InventoryTransactions.CountAsync());
        Assert.False((await PricingAllocationTestData.Get(admin, url)).CanEdit);
        var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId);
        conversion.IsActive = false; await db.SaveChangesAsync();
        var evidence = await PricingAllocationTestData.Get(admin, url);
        Assert.Equal(manualOverride ? 2.4m : 2.02m, evidence.Preview!.SystemTotal);
        Assert.Equal(manualOverride ? 123m : .51m, evidence.PhysicalLines.Single(x => x.StockDocumentLineId == editedId).CurrentUnitPriceBeforeVat);
        using var locked = await admin.Http.PostAsJsonAsync($"/admin/api/stock-documents/{seed.ReceiptId}/price-draft", new
        { rowVersion = evidence.ReceiptRowVersion, lines = confirmed.Lines.Select(x => new { stockDocumentLineId = x.Id,
            rowVersion = Convert.ToBase64String(x.RowVersion), unitPriceBeforeVat = 124m }).ToArray() });
        Assert.False(locked.IsSuccessStatusCode);
        Assert.False(evidence.CanEdit);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Helper_draft_and_applied_plan_allow_suggested_prices_without_losing_other_lines_exact_amounts(bool previouslyApplied)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url,
            PricingAllocationTestData.Request(await PricingAllocationTestData.Get(admin, url), .505m)));
        if (previouslyApplied)
            await admin.JsonAsync(HttpMethod.Post, url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion });
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        variant.CostPrice = 3.75m;
        foreach (var line in receipt.Lines.Where(x => !previouslyApplied || x.Factor == 24m))
        {
            line.UnitPriceBeforeVat = line.UnitPriceAfterVat = line.UnitCost = line.LineTotal = line.VatAmount = 0m;
        }
        receipt.SubtotalBeforeVat = receipt.TotalAmount = receipt.Lines.Sum(x => x.LineTotal);
        receipt.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var workspace = await PricingAllocationTestData.Get(admin, url);
        var html = await admin.Http.GetStringAsync($"/admin/stock-documents/{seed.ReceiptId}");
        Assert.Contains("commercial-unit-price", html);
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", new
        {
            rowVersion = workspace.ReceiptRowVersion, supplierId = receipt.SupplierId, acceptPriceVariance = true,
            lines = receipt.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = x.Factor * variant.CostPrice }).ToArray()
        });
        var confirmed = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Confirmed, confirmed.Status);
        Assert.Equal(previouslyApplied ? 181.01m : 210m, confirmed.SubtotalBeforeVat);
        Assert.Equal(previouslyApplied ? 1.01m : 30m, confirmed.Lines.Single(x => x.Factor == 4m).LineTotal);
        Assert.Equal(180m, confirmed.Lines.Single(x => x.Factor == 24m).LineTotal);
        Assert.Equal(PurchaseReceiptPricingPlanState.Confirmed, (await db.PurchaseReceiptPricingPlans.AsNoTracking().SingleAsync()).State);
    }
}

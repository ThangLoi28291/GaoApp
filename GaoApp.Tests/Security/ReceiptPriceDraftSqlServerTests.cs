using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ReceiptPriceDraftSqlServerTests
{
    [Fact]
    public async Task Price_draft_preserves_entered_before_VAT_price_and_adds_VAT_without_posting()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        receipt.Status = StockDocumentStatus.PendingApproval;
        receipt.HasVat = true;
        var line = receipt.Lines.OrderBy(x => x.Id).First();
        line.TaxRate = 10m;
        await db.SaveChangesAsync();
        var transactionCount = await db.InventoryTransactions.CountAsync();
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{receipt.Id}/price-draft", new
        {
            rowVersion = Convert.ToBase64String(receipt.RowVersion),
            lines = new[] { new { stockDocumentLineId = line.Id, rowVersion = Convert.ToBase64String(line.RowVersion), unitPriceBeforeVat = 100m } }
        });
        var persisted = await db.StockDocumentLines.AsNoTracking().SingleAsync(x => x.Id == line.Id);
        Assert.Equal(100m, persisted.UnitPriceBeforeVat);
        Assert.Equal(110m, persisted.UnitPriceAfterVat);
        Assert.Equal(decimal.Round(line.Quantity * 10m, 2, MidpointRounding.AwayFromZero), persisted.VatAmount);
        Assert.Equal(transactionCount, await db.InventoryTransactions.CountAsync());
        Assert.False(await db.PurchasePayables.AnyAsync());
    }

    [Fact]
    public async Task Draft_prices_persist_without_posting_and_reject_stale_cross_receipt_unauthorized_and_confirmed_writes()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var foreign = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, app.Stores[1]);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        document.Status = StockDocumentStatus.PendingApproval; await db.SaveChangesAsync();
        var initialCost = (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).CostPrice;
        var inventoryCount = await db.InventoryTransactions.CountAsync();
        var lines = document.Lines.OrderBy(x => x.Id).ToArray();
        var documentVersion = Convert.ToBase64String(document.RowVersion);
        var lineVersion = Convert.ToBase64String(lines[0].RowVersion);
        string Url(int id) => $"/admin/api/stock-documents/{id}/price-draft";
        object Payload(string docRv, string lineRv, decimal price, int? lineId = null) => new { rowVersion = docRv,
            lines = new[] { new { stockDocumentLineId = lineId ?? lines[0].Id, rowVersion = lineRv, unitPriceBeforeVat = price } } };
        using (var denied = await staff.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(documentVersion, lineVersion, 12345))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await admin.Http.PostAsJsonAsync(Url(foreign.ReceiptId), Payload(documentVersion, lineVersion, 12345))) Assert.False(denied.IsSuccessStatusCode);
        using (var invalid = await admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(documentVersion, lineVersion, -1))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var saved = await admin.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Payload(documentVersion, lineVersion, 12345));
        var currentRv = saved.GetProperty("rowVersion").GetString()!;
        var currentLineRv = saved.GetProperty("lineVersions").GetProperty(lines[0].Id.ToString()).GetString()!;
        Assert.NotEqual(documentVersion, currentRv); Assert.NotEqual(lineVersion, currentLineRv);
        var persisted = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(12345, persisted.Lines.Single(x => x.Id == lines[0].Id).UnitPriceBeforeVat);
        Assert.Equal(lines[1].UnitPriceBeforeVat, persisted.Lines.Single(x => x.Id == lines[1].Id).UnitPriceBeforeVat);
        Assert.Equal(StockDocumentStatus.PendingApproval, persisted.Status);
        Assert.Equal(initialCost, (await db.ProductVariants.AsNoTracking().SingleAsync(x => x.Id == store.VariantId)).CostPrice);
        Assert.Equal(inventoryCount, await db.InventoryTransactions.CountAsync());
        Assert.Equal(100, (await db.InventoryBalances.AsNoTracking().SingleAsync()).OnHandQty);
        Assert.False(await db.PurchasePayables.AnyAsync());
        var html = await admin.Http.GetStringAsync($"/admin/stock-documents/{seed.ReceiptId}");
        Assert.Contains("value=\"12345\"", html);
        using (var stale = await admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(documentVersion, lineVersion, 22222))) Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        using (var staleLine = await admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(currentRv, lineVersion, 22222))) Assert.Equal(HttpStatusCode.BadRequest, staleLine.StatusCode);
        await using var foreignDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        var foreignLine = await foreignDb.StockDocumentLines.FirstAsync(x => x.StockDocumentId == foreign.ReceiptId);
        using (var rejected = await admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), new { rowVersion = currentRv, lines = new[] {
            new { stockDocumentLineId = lines[0].Id, rowVersion = currentLineRv, unitPriceBeforeVat = 22222 },
            new { stockDocumentLineId = foreignLine.Id, rowVersion = Convert.ToBase64String(foreignLine.RowVersion), unitPriceBeforeVat = 33333 }
        } })) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(12345, (await db.StockDocumentLines.AsNoTracking().SingleAsync(x => x.Id == lines[0].Id)).UnitPriceBeforeVat);

        // Concurrent tabs: exactly one price edit may win the same receipt version.
        var concurrent = await Task.WhenAll(admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(currentRv, currentLineRv, 20000)),
            admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(currentRv, currentLineRv, 30000)));
        Assert.Single(concurrent, x => x.IsSuccessStatusCode);
        foreach (var response in concurrent) response.Dispose();
        db.ChangeTracker.Clear();
        var latest = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", new
        {
            rowVersion = Convert.ToBase64String(latest.RowVersion), supplierId = latest.SupplierId,
            isMerchandisePaid = true, acceptPriceVariance = true,
            lines = latest.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = x.UnitPriceBeforeVat }).ToArray()
        });
        db.ChangeTracker.Clear();
        latest = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Confirmed, latest.Status);
        Assert.Equal(156, (await db.InventoryBalances.AsNoTracking().SingleAsync()).OnHandQty);
        using var confirmed = await admin.Http.PostAsJsonAsync(Url(seed.ReceiptId), Payload(Convert.ToBase64String(latest.RowVersion),
            Convert.ToBase64String(latest.Lines.Single(x => x.Id == lines[0].Id).RowVersion), 99999));
        Assert.Equal(HttpStatusCode.BadRequest, confirmed.StatusCode);
    }
}

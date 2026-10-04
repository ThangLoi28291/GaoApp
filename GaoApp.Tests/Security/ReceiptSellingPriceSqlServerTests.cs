using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptSellingPriceSqlServerTests
{
    [Fact]
    public async Task Prices_save_immediately_atomically_with_audit_and_reject_stale_foreign_or_unauthorized_changes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int lineId;
        byte[] documentVersion, lineVersion;
        decimal variantCost;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            document.Status = StockDocumentStatus.PendingApproval;
            var pack = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId);
            pack.WholesalePrice = 45000;
            await db.SaveChangesAsync();
            var line = await db.StockDocumentLines.FirstAsync(x => x.StockDocumentId == seed.ReceiptId);
            line.UnitPriceBeforeVat = 44000;
            await db.SaveChangesAsync();
            lineId = line.Id; documentVersion = document.RowVersion; lineVersion = line.RowVersion;
            variantCost = (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).CostPrice;
        }
        var path = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{lineId}/selling-prices";
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var priceOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.Update));
        using var receiptOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var denied = await priceOnly.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await receiptOnly.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        var first = Read(await manager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(48000, first.Units.Single(x => x.Id == seed.PackId).Price);
        var request = Change(first, seed.PackId, 49200, seed.CartonId, 293400);
        using (var denied = await priceOnly.Http.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await receiptOnly.Http.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await manager.Http.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var saved = Read(await manager.JsonAsync(HttpMethod.Post, path, request));
        Assert.Equal(49200, saved.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Equal(293400, saved.Units.Single(x => x.Id == seed.CartonId).Price);
        Assert.Equal(3, saved.History.Count);
        Assert.Equal(12300, saved.ProductPrice);
        var reviewPath = $"/admin/api/stock-documents/{seed.ReceiptId}/selling-price-reviews";
        var reviews = (await manager.JsonAsync(HttpMethod.Get, reviewPath)).Deserialize<ReceiptPriceReviewsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(reviews.Lines.Single(x => x.LineId == lineId).Reviewed);
        Assert.True(reviews.Lines.Single(x => x.LineId == lineId).HasChanges);
        using var receiver = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        var safeReviews = (await receiver.JsonAsync(HttpMethod.Get, reviewPath)).Deserialize<ReceiptPriceReviewsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(safeReviews.Lines, x => { Assert.Null(x.CurrentBaseCost); Assert.Null(x.ReviewedBaseCost); });
        using (var denied = await foreign.Http.GetAsync(reviewPath)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.NotEqual(first.Units.Single(x => x.Id == seed.PackId).RowVersion, saved.Units.Single(x => x.Id == seed.PackId).RowVersion);
        using (var stale = await manager.Http.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // A stale second unit must prevent saving the valid first unit too.
        var staleBatch = Change(saved, seed.PackId, 51000, seed.CartonId, 300000);
        staleBatch.Units[1].RowVersion = first.Units.Single(x => x.Id == seed.CartonId).RowVersion;
        using (var stale = await manager.Http.PostAsJsonAsync(path, staleBatch)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var invalid = Change(saved, seed.PackId, -1);
        using (var denied = await manager.Http.PostAsJsonAsync(path, invalid))
            Assert.Contains(denied.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        invalid = Change(saved, seed.PackId, 123.456m);
        using (var denied = await manager.Http.PostAsJsonAsync(path, invalid)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        invalid = Change(saved, seed.PackId, 50000);
        invalid.Units.Add(invalid.Units[0]);
        using (var denied = await manager.Http.PostAsJsonAsync(path, invalid)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        invalid = Change(saved, seed.PackId, 50000); invalid.Units[0].Id = int.MaxValue;
        using (var denied = await manager.Http.PostAsJsonAsync(path, invalid)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);

        // Barcode lookup used by the catalog sees the new selling price without receipt approval.
        var lookup = await manager.JsonAsync(HttpMethod.Post, "/Admin/ProductUnitConversion/LookupBarcode", new { barcode = "INTERNAL-PACK" });
        Assert.Equal(49200, lookup.GetProperty("data").GetProperty("sellPrice").GetDecimal());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            Assert.Equal(StockDocumentStatus.PendingApproval, document.Status);
            Assert.Equal(documentVersion, document.RowVersion);
            Assert.Equal(lineVersion, (await db.StockDocumentLines.SingleAsync(x => x.Id == lineId)).RowVersion);
            Assert.Equal(variantCost, (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).CostPrice);
            Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            var pack = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId);
            Assert.Equal(49200, pack.Price); Assert.Equal(45000, pack.WholesalePrice);
            Assert.Equal(3, await db.AuditLogs.CountAsync(x => x.EntityName == "ReceiptSellingPrice"));
            Assert.Equal(12300, (await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId)).Product.BasePrice);
            // A changed fallback price invalidates the captured snapshot as well.
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).Price = 11111;
            await db.SaveChangesAsync();
        }
        using (var stale = await manager.Http.PostAsJsonAsync(path, Change(saved, seed.PackId, 55000))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var refreshed = Read(await manager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(49200, refreshed.Units.Single(x => x.Id == seed.PackId).Price);
        reviews = (await manager.JsonAsync(HttpMethod.Get, reviewPath)).Deserialize<ReceiptPriceReviewsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(reviews.Lines.Single(x => x.LineId == lineId).Reviewed);
        Assert.True(reviews.Lines.Single(x => x.LineId == lineId).NeedsAttention);
        var wholesaleChange = Change(refreshed, seed.PackId, 49200);
        wholesaleChange.Units[0].UpdateWholesalePrice = true;
        wholesaleChange.Units[0].WholesalePrice = 47500;
        var wholesaleSaved = Read(await manager.JsonAsync(HttpMethod.Post, path, wholesaleChange));
        Assert.Equal(47500, wholesaleSaved.Units.Single(x => x.Id == seed.PackId).WholesalePrice);
        Assert.Equal(12300, wholesaleSaved.ProductPrice);
        var invalidWholesale = Change(wholesaleSaved, seed.PackId, 49200);
        invalidWholesale.Units[0].UpdateWholesalePrice = true; invalidWholesale.Units[0].WholesalePrice = 0;
        using (var rejected = await manager.Http.PostAsJsonAsync(path, invalidWholesale))
            Assert.Contains(rejected.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        var reviewOnly = Change(wholesaleSaved, seed.PackId, 999999);
        reviewOnly.ReviewOnly = true; reviewOnly.Units[0].UpdateWholesalePrice = true; reviewOnly.Units[0].WholesalePrice = 999999;
        var reviewed = Read(await manager.JsonAsync(HttpMethod.Post, path, reviewOnly));
        Assert.Equal(49200, reviewed.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Equal(47500, reviewed.Units.Single(x => x.Id == seed.PackId).WholesalePrice);
        Assert.Equal(12300, reviewed.ProductPrice);
        var clearWholesale = Change(reviewed, seed.PackId, 49200);
        clearWholesale.Units[0].UpdateWholesalePrice = true;
        var cleared = Read(await manager.JsonAsync(HttpMethod.Post, path, clearWholesale));
        Assert.Null(cleared.Units.Single(x => x.Id == seed.PackId).WholesalePrice);
        await using (var costDb = app.Database.CreateTenantContext(store.StoreId))
        {
            (await costDb.StockDocumentLines.SingleAsync(x => x.Id == lineId)).UnitPriceBeforeVat = 50000;
            await costDb.SaveChangesAsync();
        }
        reviews = (await manager.JsonAsync(HttpMethod.Get, reviewPath)).Deserialize<ReceiptPriceReviewsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(reviews.Lines.Single(x => x.LineId == lineId).NeedsAttention);
        Assert.False(reviews.Lines.Single(x => x.LineId == lineId).Reviewed);

        // Older products without conversions still sell using the variant/base price.
        int fallbackLineId, fallbackVariantId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var source = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            var variant = new GaoApp.Domain.Entities.ProductVariant { StoreId = store.StoreId,
                ProductId = source.ProductId, Sku = "PRICE-FALLBACK", Price = 12000, CostPrice = 10000 };
            db.Add(variant); await db.SaveChangesAsync(); fallbackVariantId = variant.Id;
            var line = new GaoApp.Domain.Entities.StockDocumentLine { StockDocumentId = seed.ReceiptId,
                ProductVariantId = variant.Id, ProductNameSnapshot = "Đơn vị gốc", LineNo = 3,
                Quantity = 1, BaseQuantity = 1, Factor = 1, UnitPriceBeforeVat = 11000 };
            db.Add(line); await db.SaveChangesAsync(); fallbackLineId = line.Id;
        }
        var fallbackPath = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{fallbackLineId}/selling-prices";
        var fallback = Read(await manager.JsonAsync(HttpMethod.Get, fallbackPath));
        Assert.Equal(0, Assert.Single(fallback.Units).Id);
        var fallbackRequest = Change(fallback, 0, 12300);
        fallbackRequest.Units[0].UpdateWholesalePrice = true; fallbackRequest.Units[0].WholesalePrice = 11500;
        var fallbackSaved = Read(await manager.JsonAsync(HttpMethod.Post, fallbackPath, fallbackRequest));
        Assert.Equal(12300, Assert.Single(fallbackSaved.Units).Price);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        var fallbackVariant = await final.ProductVariants.SingleAsync(x => x.Id == fallbackVariantId);
        Assert.Equal(12300, fallbackVariant.Price); Assert.Equal(10000, fallbackVariant.CostPrice);
        Assert.Equal(11500, fallbackVariant.WholesalePrice);
        Assert.False(await final.ProductUnitConversions.AnyAsync(x => x.ProductVariantId == fallbackVariantId));

        // POS must actually use the newly configured wholesale fallback, not just display it in the editor.
        await manager.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var cart = await manager.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = cart.GetProperty("orderId").GetInt32();
        await using (var posDb = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new GaoApp.Domain.Entities.Customer { StoreId = store.StoreId, Name = "Khách sỉ thử giá", PriceTier = "WHOLESALE" };
            posDb.Add(customer); await posDb.SaveChangesAsync();
            (await posDb.Orders.SingleAsync(x => x.Id == orderId)).CustomerId = customer.Id;
            posDb.InventoryBalances.Add(new GaoApp.Domain.Entities.InventoryBalance { StoreId = store.StoreId,
                WarehouseId = store.WarehouseId, ProductVariantId = fallbackVariantId, OnHandQty = 100 });
            await posDb.SaveChangesAsync();
        }
        await manager.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={fallbackVariantId}&qty=1", new { });
        await using var posCheck = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(11500, (await posCheck.OrderLines.SingleAsync(x => x.OrderId == orderId)).UnitPrice);

        // Normalizing the shared Product price must not reduce an inherited source unit's retail price.
        await using (var inheritedDb = app.Database.CreateTenantContext(store.StoreId))
        {
            var inheritedVariant = await inheritedDb.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            inheritedVariant.Price = null; inheritedVariant.Product.BasePrice = 48000;
            (await inheritedDb.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Price = null;
            await inheritedDb.SaveChangesAsync();
        }
        var inherited = Read(await manager.JsonAsync(HttpMethod.Get, path));
        var inheritedRequest = Change(inherited, seed.PackId, 48000);
        inheritedRequest.Units[0].UpdateWholesalePrice = true; inheritedRequest.Units[0].WholesalePrice = 47000;
        var inheritedSaved = Read(await manager.JsonAsync(HttpMethod.Post, path, inheritedRequest));
        Assert.Equal(12000, inheritedSaved.ProductPrice);
        Assert.Equal(48000, inheritedSaved.Units.Single(x => x.Id == seed.PackId).Price);
        await manager.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&productUnitConversionId={seed.PackId}&qty=1", new { });
        Assert.Equal(47000, (await posCheck.OrderLines.AsNoTracking().SingleAsync(x => x.OrderId == orderId && x.VariantId == store.VariantId)).UnitPrice);
    }

    private static ReceiptSellingPriceDto Read(JsonElement json) =>
        json.Deserialize<ReceiptSellingPriceDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static UpdateReceiptSellingPricesRequest Change(ReceiptSellingPriceDto data, int id, decimal price,
        int? secondId = null, decimal secondPrice = 0)
    {
        var result = new UpdateReceiptSellingPricesRequest { ProductVersion = data.ProductVersion,
            VariantVersion = data.VariantVersion, LineVersion = data.LineVersion, EstimatedBaseCost = 11000,
            CatalogVersion = data.CatalogVersion, ProductPriceUnitId = data.ProductPriceUnitId };
        result.Units.Add(new() { Id = id, Price = price, RowVersion = data.Units.Single(x => x.Id == id).RowVersion });
        if (secondId.HasValue) result.Units.Add(new() { Id = secondId.Value, Price = secondPrice,
            RowVersion = data.Units.Single(x => x.Id == secondId).RowVersion });
        return result;
    }
}

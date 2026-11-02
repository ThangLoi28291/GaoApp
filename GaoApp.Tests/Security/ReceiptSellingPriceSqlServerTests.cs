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
    public async Task Inconsistent_purchase_link_requires_purchase_permission_and_blocks_price_and_review_writes_without_side_effects()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int lineId;
        string receiptVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            var order = new GaoApp.Domain.Entities.PurchaseOrder
            {
                StoreId = store.StoreId, OrderNumber = "SELL-LEGACY-" + Guid.NewGuid().ToString("N")[..10],
                SupplierId = receipt.SupplierId, ExpectedWarehouseId = store.WarehouseId,
                LegalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId
            };
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            receipt.Status = StockDocumentStatus.PendingApproval;
            receipt.PurchaseOrderId = order.Id;
            receipt.ReceiptSource = PurchaseReceiptSource.Direct;
            await db.SaveChangesAsync();
            lineId = receipt.Lines.Single(x => x.ProductUnitConversionId == seed.PackId).Id;
            receiptVersion = Convert.ToBase64String(receipt.RowVersion);
        }

        using var directManager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.Approve, PermissionCodes.Catalog.Product.Update));
        using var purchaseManager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Catalog.Product.Update));
        var path = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{lineId}/selling-prices";
        var reviewsPath = $"/admin/api/stock-documents/{seed.ReceiptId}/selling-price-reviews";
        var before = Read(await purchaseManager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(PurchaseReceiptSource.PurchaseOrder, before.ReceiptSource);
        var beforeReviews = await purchaseManager.JsonAsync(HttpMethod.Get, reviewsPath);
        Assert.Equal(PurchaseReceiptSource.PurchaseOrder, beforeReviews.Deserialize<ReceiptPriceReviewsDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ReceiptSource);
        using (var denied = await directManager.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await directManager.Http.GetAsync(reviewsPath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var newPrice = before.Units.Single(x => x.Id == seed.PackId).Price + 1000m;
        foreach (var reviewOnly in new[] { false, true })
        {
            var change = Change(before, seed.PackId, newPrice);
            change.UpdateProductPrice = false;
            change.ReviewOnly = reviewOnly;
            using (var denied = await directManager.Http.PostAsJsonAsync(path, change))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var blocked = await purchaseManager.Http.PostAsJsonAsync(path, change))
            {
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                Assert.Contains("Nguồn phiếu không khớp", await blocked.Content.ReadAsStringAsync());
            }
        }

        var unchanged = Read(await purchaseManager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(before.CatalogVersion, unchanged.CatalogVersion);
        Assert.Equal(before.ProductVersion, unchanged.ProductVersion);
        Assert.Equal(before.VariantVersion, unchanged.VariantVersion);
        Assert.Equal(before.LineVersion, unchanged.LineVersion);
        Assert.Equal(before.Units, unchanged.Units);
        Assert.Equal(before.History, unchanged.History);
        Assert.Equal(beforeReviews.GetRawText(), (await purchaseManager.JsonAsync(HttpMethod.Get, reviewsPath)).GetRawText());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.False(await db.AuditLogs.AnyAsync(x => x.EntityName == "ReceiptSellingPrice" || x.EntityName == "ReceiptPriceReview"));
            var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            Assert.Equal(receiptVersion, Convert.ToBase64String(receipt.RowVersion));
            Assert.Equal(PurchaseReceiptSource.Direct, receipt.ReceiptSource);
            Assert.NotNull(receipt.PurchaseOrderId);
            receipt.ReceiptSource = PurchaseReceiptSource.PurchaseOrder;
            await db.SaveChangesAsync();
        }

        // The consistency guard must still permit the ordinary, correctly linked PO price workflow.
        var valid = Read(await purchaseManager.JsonAsync(HttpMethod.Get, path));
        var validChange = Change(valid, seed.PackId, newPrice);
        validChange.UpdateProductPrice = false;
        var saved = Read(await purchaseManager.JsonAsync(HttpMethod.Post, path, validChange));
        Assert.Equal(PurchaseReceiptSource.PurchaseOrder, saved.ReceiptSource);
        Assert.Equal(newPrice, saved.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Single(saved.History);
    }

    [Fact]
    public async Task Unit_only_saves_do_not_normalize_shared_Product_or_materialize_unselected_inherited_prices()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status = StockDocumentStatus.PendingApproval;
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.Price = null; variant.Product.BasePrice = 48000;
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Price = null;
            await db.SaveChangesAsync();
            lineId = (await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == seed.ReceiptId &&
                x.ProductUnitConversionId == seed.PackId)).Id;
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var path = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{lineId}/selling-prices";
        var first = Read(await manager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(seed.PackId, first.ProductPriceUnitId);
        var cartonChange = Change(first, seed.CartonId, 310000);
        cartonChange.UpdateProductPrice = false;
        var saved = Read(await manager.JsonAsync(HttpMethod.Post, path, cartonChange));
        Assert.Equal(48000, saved.ProductPrice);
        Assert.Equal(48000, saved.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Equal(310000, saved.Units.Single(x => x.Id == seed.CartonId).Price);
        Assert.Equal(first.ProductVersion, saved.ProductVersion);
        Assert.Equal(first.VariantVersion, saved.VariantVersion);
        Assert.Equal(first.Units.Single(x => x.Id == seed.PackId).RowVersion,
            saved.Units.Single(x => x.Id == seed.PackId).RowVersion);
        Assert.Single(saved.History);
        await using (var check = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Null((await check.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Price);
            Assert.Null((await check.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).Price);
        }

        // A plain review with no chosen unit must not normalize an existing Product/unit mismatch.
        var noChanges = Change(saved, seed.PackId, 48000);
        noChanges.UpdateProductPrice = false; noChanges.Units.Clear();
        var reviewed = Read(await manager.JsonAsync(HttpMethod.Post, path, noChanges));
        Assert.Equal(saved.ProductVersion, reviewed.ProductVersion);
        Assert.Equal(saved.CatalogVersion, reviewed.CatalogVersion);
        Assert.Single(reviewed.History);

        // Editing only wholesale also keeps the source's inherited retail price inherited.
        var wholesaleOnly = Change(reviewed, seed.PackId, 48000);
        wholesaleOnly.UpdateProductPrice = false;
        wholesaleOnly.Units[0].UpdateWholesalePrice = true; wholesaleOnly.Units[0].WholesalePrice = 47000;
        var wholesaleSaved = Read(await manager.JsonAsync(HttpMethod.Post, path, wholesaleOnly));
        Assert.Equal(48000, wholesaleSaved.ProductPrice);
        Assert.Equal(47000, wholesaleSaved.Units.Single(x => x.Id == seed.PackId).WholesalePrice);
        Assert.Equal(2, wholesaleSaved.History.Count);
        await using (var check = app.Database.CreateTenantContext(store.StoreId))
            Assert.Null((await check.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Price);

        foreach (var updateProduct in new[] { false, true })
        {
            var reviewOnly = Change(wholesaleSaved, seed.PackId, 999999);
            reviewOnly.UpdateProductPrice = updateProduct; reviewOnly.ReviewOnly = true;
            reviewOnly.Units[0].UpdateWholesalePrice = true; reviewOnly.Units[0].WholesalePrice = 999999;
            var review = Read(await manager.JsonAsync(HttpMethod.Post, path, reviewOnly));
            Assert.Equal(wholesaleSaved.CatalogVersion, review.CatalogVersion);
            Assert.Equal(48000, review.ProductPrice);
            Assert.Equal(48000, review.Units.Single(x => x.Id == seed.PackId).Price);
            Assert.Equal(47000, review.Units.Single(x => x.Id == seed.PackId).WholesalePrice);
            Assert.Equal(2, review.History.Count);
        }

        // Explicit opt-in retains the previous normalization and source-preservation behavior.
        var syncProduct = Change(wholesaleSaved, seed.PackId, 48000);
        Assert.True(syncProduct.UpdateProductPrice);
        syncProduct.Units.Clear();
        var normalized = Read(await manager.JsonAsync(HttpMethod.Post, path, syncProduct));
        Assert.Equal(12000, normalized.ProductPrice);
        Assert.Equal(48000, normalized.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Equal(310000, normalized.Units.Single(x => x.Id == seed.CartonId).Price);
        Assert.Equal(4, normalized.History.Count);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(48000, (await final.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Price);
        Assert.Equal(47000, (await final.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).WholesalePrice);
    }

    [Fact]
    public async Task Unit_only_saves_skip_unused_Product_conversion_validation_but_keep_source_and_version_checks()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status = StockDocumentStatus.PendingApproval;
            await db.SaveChangesAsync();
            lineId = (await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == seed.ReceiptId &&
                x.ProductUnitConversionId == seed.CartonId)).Id;
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var path = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{lineId}/selling-prices";
        var first = Read(await manager.JsonAsync(HttpMethod.Get, path));
        var unitOnly = Change(first, seed.CartonId, 0.02m);
        unitOnly.UpdateProductPrice = false; unitOnly.ProductPriceUnitId = seed.CartonId;
        var saved = Read(await manager.JsonAsync(HttpMethod.Post, path, unitOnly));
        Assert.Equal(0.02m, saved.Units.Single(x => x.Id == seed.CartonId).Price);
        Assert.Equal(first.ProductPrice, saved.ProductPrice);
        Assert.Equal(first.Units.Single(x => x.Id == seed.PackId).Price,
            saved.Units.Single(x => x.Id == seed.PackId).Price);
        Assert.Single(saved.History);
        using (var stale = await manager.Http.PostAsJsonAsync(path, unitOnly))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // This source rounds to zero per base unit, which is relevant only when updating Product.
        var invalidProduct = Change(saved, seed.CartonId, 0.02m);
        invalidProduct.UpdateProductPrice = true; invalidProduct.ProductPriceUnitId = seed.CartonId;
        using (var rejected = await manager.Http.PostAsJsonAsync(path, invalidProduct))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var invalidSource = Change(saved, seed.CartonId, 0.03m);
        invalidSource.UpdateProductPrice = false; invalidSource.ProductPriceUnitId = int.MaxValue;
        using (var rejected = await manager.Http.PostAsJsonAsync(path, invalidSource))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var unchanged = Read(await manager.JsonAsync(HttpMethod.Get, path));
        Assert.Equal(saved.CatalogVersion, unchanged.CatalogVersion);
        Assert.Single(unchanged.History);
    }

    [Fact]
    public async Task Reviews_include_all_units_and_warn_when_carton_wholesale_is_under_cost_while_default_unit_is_profitable()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var line = await db.StockDocumentLines.FirstAsync(x => x.StockDocumentId == seed.ReceiptId);
            line.UnitPriceBeforeVat = 44000; lineId = line.Id;
            var pack = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId);
            pack.Price = 48000; pack.WholesalePrice = 45000;
            var carton = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.CartonId);
            carton.Price = 300000; carton.WholesalePrice = 200000;
            await db.SaveChangesAsync();
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var json = await manager.JsonAsync(HttpMethod.Get, $"/admin/api/stock-documents/{seed.ReceiptId}/selling-price-reviews");
        var review = json.Deserialize<ReceiptPriceReviewsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Lines.Single(x => x.LineId == lineId);
        Assert.True(review.BelowCost);
        var packPrice = review.Units.Single(x => x.Id == seed.PackId);
        var cartonPrice = review.Units.Single(x => x.Id == seed.CartonId);
        Assert.True(packPrice.Price > review.CurrentBaseCost * packPrice.Factor);
        Assert.True(cartonPrice.Price > review.CurrentBaseCost * cartonPrice.Factor);
        Assert.True(cartonPrice.WholesalePrice < review.CurrentBaseCost * cartonPrice.Factor);
    }
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

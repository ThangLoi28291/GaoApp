using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptBarcodeProposalSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/barcode-proposals";
    private static object Proposal(int conversion, string code, decimal factor = 4) => new { productUnitConversionId = conversion, barcode = code, factor };
    private static int RequestId(JsonElement state, string code) => state.GetProperty("items").EnumerateArray()
        .Single(x => x.GetProperty("suggestedBarcode").GetString() == code).GetProperty("id").GetInt32();

    [Fact]
    public async Task Receiving_lookup_finds_unaccented_names_with_missing_or_stale_normalization()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Update));
        var seed = await SeedAsync(app, store);
        // Mirror a legacy/imported row; searching cannot depend on this field being repaired first.
        foreach (var normalized in new string?[] { null, "old-import-name" })
        {
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
            {
                (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).ProductVariantNameNormalized = normalized;
                await db.SaveChangesAsync();
            }
            foreach (var term in new[] { "sua tuoi", "SUA TUOI KHONG DUONG", "Sữa tươi không đường", "khong duong", "loc", "thung" })
            {
                var response = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/lookup?term=" + Uri.EscapeDataString(term));
                var results = response.GetProperty("results").EnumerateArray().ToList();
                Assert.NotEmpty(results);
                Assert.All(results, x => Assert.Equal(store.VariantId, x.GetProperty("productVariantId").GetInt32()));
            }
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.Product.Name = "Đậu nành đóng hộp";
            await db.SaveChangesAsync();
        }
        var parentName = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/lookup?catalogOnly=true&term=dau%20nanh");
        Assert.NotEmpty(parentName.GetProperty("results").EnumerateArray());
        var barcode = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/lookup?term=INTERNAL-PACK");
        var exact = Assert.Single(barcode.GetProperty("results").EnumerateArray());
        Assert.Equal(seed.PackId, exact.GetProperty("productUnitConversionId").GetInt32());
        Assert.Equal(4, exact.GetProperty("factor").GetDecimal());
    }

    [Fact]
    public async Task Employee_proposes_pack_and_carton_manager_adds_aliases_without_replacing_internal_codes()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var employeeAccount = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Update);
        using var employee = await app.LoginAsync(employeeAccount);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Approve));
        var seed = await SeedAsync(app, store);
        const string packCode = "0001234567890", cartonCode = "CTN-ABC-0001";
        var exact = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/lookup?catalogOnly=true&term=INTERNAL-PACK");
        Assert.Single(exact.GetProperty("results").EnumerateArray());
        var units = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + $"/products/{store.VariantId}/units");
        Assert.Equal(2, units.GetProperty("items").GetArrayLength());
        Assert.Contains(units.GetProperty("items").EnumerateArray(), x => x.GetProperty("barcode").GetString() == "INTERNAL-CARTON" && x.GetProperty("factor").GetDecimal() == 24);
        await employee.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, packCode));
        await employee.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.CartonId, cartonCode, 24));
        await employee.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, packCode));
        var state = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.Equal(2, state.GetProperty("items").GetArrayLength());
        Assert.All(state.GetProperty("items").EnumerateArray(), x => Assert.EndsWith("Z", x.GetProperty("requestedAtUtc").GetString()));
        Assert.All(state.GetProperty("items").EnumerateArray(), x => Assert.Equal(employeeAccount.UserId, x.GetProperty("requestedByUserId").GetInt32()));
        var local = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/lookup?term=" + packCode);
        Assert.Equal(4, local.GetProperty("results")[0].GetProperty("factor").GetDecimal());
        Assert.Equal(packCode, local.GetProperty("results")[0].GetProperty("barcode").GetString());
        var other = await AddDocument(app, store, seed.PackId);
        var notGlobal = await employee.JsonAsync(HttpMethod.Get, Url(other) + "/lookup?term=" + packCode);
        Assert.Equal(0, notGlobal.GetProperty("results").GetArrayLength());
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.PendingApproval);
        foreach (var code in new[] { packCode, cartonCode })
            await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + $"/{RequestId(state, code)}/review", new { approve = true, note = "Đã đối chiếu bao bì" });
        var global = await employee.JsonAsync(HttpMethod.Get, Url(other) + "/lookup?term=" + cartonCode);
        Assert.Equal(seed.CartonId, global.GetProperty("results")[0].GetProperty("productUnitConversionId").GetInt32());
        Assert.Equal(24, global.GetProperty("results")[0].GetProperty("factor").GetDecimal());
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var codes = await db.ProductVariantUnitBarcodes.ToListAsync();
        Assert.Equal(4, codes.Count);
        Assert.Equal(2, codes.Count(x => x.IsPrimary && x.BarcodeType == BarcodeType.Internal));
        Assert.Equal(2, codes.Count(x => !x.IsPrimary && x.BarcodeType == BarcodeType.Supplier));
        Assert.Equal(2, await db.ProductVariantBarcodeHistories.CountAsync());
        Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Fact]
    public async Task Concurrent_review_is_atomic_and_another_receipt_can_add_another_code_to_same_unit()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var manager2 = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var seed = await SeedAsync(app, store);
        const string code = "MFG-PACK-1";
        await Task.WhenAll(manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, code)),
            manager2.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, code)));
        var state = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.Single(state.GetProperty("items").EnumerateArray());
        var review = Url(seed.ReceiptId) + $"/{RequestId(state, code)}/review";
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.PendingApproval);
        await Task.WhenAll(manager.JsonAsync(HttpMethod.Post, review, new { approve = true }), manager2.JsonAsync(HttpMethod.Post, review, new { approve = true }));
        var other = await AddDocument(app, store, seed.PackId);
        await manager.JsonAsync(HttpMethod.Post, Url(other), Proposal(seed.PackId, "MFG-PACK-2"));
        state = await manager.JsonAsync(HttpMethod.Get, Url(other));
        await ChangeStatus(app, store, other, StockDocumentStatus.PendingApproval);
        await manager.JsonAsync(HttpMethod.Post, Url(other) + $"/{RequestId(state, "MFG-PACK-2")}/review", new { approve = true });
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(2, await db.ProductVariantUnitBarcodes.CountAsync(x => x.BarcodeType == BarcodeType.Supplier));
        Assert.Equal(2, await db.ProductVariantBarcodeHistories.CountAsync());
        Assert.Equal(2, await db.ProductBarcodeVerificationRequests.CountAsync(x => x.Status == BarcodeVerificationRequestStatus.Approved));
    }

    [Fact]
    public async Task Rejected_barcode_does_not_change_receipt_quantities_and_commercial_approval_still_posts_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var seed = await SeedAsync(app, store);
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, "REJECT-ME"));
        var state = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.PendingApproval);
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + $"/{RequestId(state, "REJECT-ME")}/review", new { approve = false, note = "Bao bì không đúng mã" });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            Assert.Equal(56, doc.Lines.Sum(x => x.BaseQuantity));
            Assert.False(await db.ProductVariantUnitBarcodes.AnyAsync(x => x.Barcode == "REJECT-ME"));
            await manager.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", new
            {
                rowVersion = Convert.ToBase64String(doc.RowVersion), supplierId = doc.SupplierId, isMerchandisePaid = true,
                acceptPriceVariance = true,
                lines = doc.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = 10m, taxRate = 0m }).ToArray()
            });
        }
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(StockDocumentStatus.Confirmed, (await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        Assert.Equal(156, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Fact]
    public async Task Permissions_csrf_cross_store_code_conflicts_and_stale_conversion_are_blocked()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var seed = await SeedAsync(app, store);
        using (var response = await denied.Http.GetAsync(Url(seed.ReceiptId))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await denied.Http.GetAsync(Url(seed.ReceiptId) + $"/products/{store.VariantId}/units")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await outsider.Http.GetAsync(Url(seed.ReceiptId) + $"/products/{store.VariantId}/units")) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var foreignVariant = await staff.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + $"/products/{app.Stores[1].VariantId}/units");
        Assert.Empty(foreignVariant.GetProperty("items").EnumerateArray());
        using (var response = await outsider.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.PackId, "CROSS"))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.CartonId, "INTERNAL-PACK", 24))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.PackId, "BAD", 24))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var token = staff.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); staff.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await staff.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.PackId, "NO-CSRF"))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        staff.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        await staff.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, "STALE-PACK"));
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.CartonId, "STALE-PACK", 24))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var state = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        var review = Url(seed.ReceiptId) + $"/{RequestId(state, "STALE-PACK")}/review";
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.PendingApproval);
        using (var response = await staff.Http.PostAsJsonAsync(review, new { approve = true })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).Factor = 6; await db.SaveChangesAsync(); }
        using (var response = await manager.Http.PostAsJsonAsync(review, new { approve = true })) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await check.ProductVariantUnitBarcodes.AnyAsync(x => x.Barcode == "STALE-PACK"));
        Assert.Equal(BarcodeVerificationRequestStatus.Pending, (await check.ProductBarcodeVerificationRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Purchase_order_receipt_requires_purchase_permission_and_current_owner_lease()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var owner = await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Update);
        using var employee = await app.LoginAsync(owner);
        using var wrongOwner = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Update));
        using var direct = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        var seed = await SeedAsync(app, store); var lease = Guid.NewGuid();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            var po = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "PO-BARCODE", SupplierId = doc.SupplierId!.Value,
                ExpectedWarehouseId = doc.WarehouseId, LegalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == doc.WarehouseId)).LegalEntityId };
            db.Add(po); await db.SaveChangesAsync();
            doc.PurchaseOrderId = po.Id; doc.ReceiptSource = PurchaseReceiptSource.PurchaseOrder;
            doc.ReceivingSessionState = ReceivingSessionState.Active; doc.ReceivingOwnerUserId = owner.UserId;
            doc.ReceivingLeaseToken = lease; doc.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }
        var body = new { productUnitConversionId = seed.PackId, barcode = "PO-MFG", factor = 4, leaseToken = lease };
        using (var response = await direct.Http.PostAsJsonAsync(Url(seed.ReceiptId), body)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await wrongOwner.Http.PostAsJsonAsync(Url(seed.ReceiptId), body)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await employee.Http.PostAsJsonAsync(Url(seed.ReceiptId), Proposal(seed.PackId, "PO-MFG"))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await employee.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), body);
        var units = await employee.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + $"/products/{store.VariantId}/units");
        Assert.Equal(2, units.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Inline_review_rejects_orphan_and_conflicting_catalog_assignment_while_legacy_catalog_review_remains_atomic()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var catalog = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Barcode.View, PermissionCodes.Catalog.Barcode.Update));
        var seed = await SeedAsync(app, store);
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.PackId, "ORPHAN"));
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId), Proposal(seed.CartonId, "RACE-CATALOG", 24));
        var state = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        var orphanId = RequestId(state, "ORPHAN"); var conflictId = RequestId(state, "RACE-CATALOG");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var line = await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == seed.ReceiptId && x.ProductUnitConversionId == seed.PackId);
            line.IsDeleted = true;
            db.ProductVariantUnitBarcodes.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = seed.PackId,
                Barcode = "RACE-CATALOG", IsPrimary = false, IsActive = true });
            await db.SaveChangesAsync();
        }
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.PendingApproval);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + $"/{orphanId}/review", new { approve = true })) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + $"/{conflictId}/review", new { approve = true })) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using (var check = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(2, await check.ProductBarcodeVerificationRequests.CountAsync(x => x.Status == BarcodeVerificationRequestStatus.Pending));
            Assert.False(await check.ProductVariantUnitBarcodes.AnyAsync(x => x.Barcode == "ORPHAN"));
            Assert.Empty(await check.ProductVariantBarcodeHistories.ToListAsync());
        }
        await ChangeStatus(app, store, seed.ReceiptId, StockDocumentStatus.Draft);
        await catalog.JsonAsync(HttpMethod.Post, $"/admin/barcode-normalization/{orphanId}/approve", new { managerNote = "Kiểm tra danh mục riêng" });
        await catalog.JsonAsync(HttpMethod.Post, $"/admin/barcode-normalization/{orphanId}/approve", new { managerNote = "retry" });
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(1, await final.ProductVariantUnitBarcodes.CountAsync(x => x.Barcode == "ORPHAN"));
        Assert.Equal(1, await final.ProductVariantBarcodeHistories.CountAsync());
        Assert.Equal(BarcodeVerificationRequestStatus.Approved, (await final.ProductBarcodeVerificationRequests.SingleAsync(x => x.Id == orphanId)).Status);
    }

    internal sealed record Seed(int ReceiptId, int PackId, int CartonId);
    internal static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit).SingleAsync(x => x.Id == store.VariantId);
        variant.ProductVariantName = "Sữa tươi không đường"; variant.Product.Name = "Sữa tươi không đường"; variant.Product.BaseUnit.Name = "Hộp";
        var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, Unit = new Unit { StoreId = store.StoreId, Code = "PACK", Name = "Lốc" }, Factor = 4, Price = 48000 };
        var carton = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, Unit = new Unit { StoreId = store.StoreId, Code = "CARTON", Name = "Thùng" }, Factor = 24, Price = 280000 };
        db.AddRange(pack, carton); await db.SaveChangesAsync();
        db.AddRange(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = pack.Id, Barcode = "INTERNAL-PACK", BarcodeType = BarcodeType.Internal, IsPrimary = true, IsActive = true },
            new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = carton.Id, Barcode = "INTERNAL-CARTON", BarcodeType = BarcodeType.Internal, IsPrimary = true, IsActive = true });
        await db.SaveChangesAsync();
        var receipt = await AddDocument(app, store, pack.Id, carton.Id);
        return new(receipt, pack.Id, carton.Id);
    }

    private static async Task<int> AddDocument(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, params int[] conversions)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var supplier = await db.Suppliers.SingleAsync();
        var units = await db.ProductUnitConversions.Include(x => x.Unit).Where(x => conversions.Contains(x.Id)).ToListAsync();
        var doc = new StockDocument { StoreId = store.StoreId, DocumentNo = "BC-" + Guid.NewGuid().ToString("N")[..10], WarehouseId = store.WarehouseId,
            SupplierId = supplier.Id, ReceiptSource = PurchaseReceiptSource.Direct, DirectReceiptReason = "Bổ sung mã hãng", IsMerchandisePaid = true,
            Lines = units.Select((x, i) => new StockDocumentLine { ProductVariantId = store.VariantId, ProductUnitConversionId = x.Id, UnitId = x.UnitId,
                UnitNameSnapshot = x.Unit.Name, ProductNameSnapshot = "Sữa tươi không đường", Factor = x.Factor, Quantity = 2, BaseQuantity = 2 * x.Factor,
                UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 20, LineNo = i + 1 }).ToList() };
        db.Add(doc); await db.SaveChangesAsync(); return doc.Id;
    }

    private static async Task ChangeStatus(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId, StockDocumentStatus status)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        (await db.StockDocuments.SingleAsync(x => x.Id == receiptId)).Status = status; await db.SaveChangesAsync();
    }
}

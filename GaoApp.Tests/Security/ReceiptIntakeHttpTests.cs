using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptIntakeHttpTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/intake";
    private static string Version(JsonElement state) => state.GetProperty("documentRowVersion").GetString()!;

    [Fact]
    public async Task Draft_supports_authorized_immediate_creation_and_removal_without_stock_posting()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        int documentId, unitId, categoryId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = (await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId)).Product;
            unitId = product.BaseUnitId; categoryId = product.CategoryId;
            var document = new StockDocument { StoreId = store.StoreId, DocumentNo = "INTAKE-DIRECT", WarehouseId = store.WarehouseId,
                SupplierId = (await db.Suppliers.SingleAsync()).Id, ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Nhập mới", IsMerchandisePaid = true };
            db.Add(document); await db.SaveChangesAsync(); documentId = document.Id;
        }
        var state = (await manager.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("state");
        var direct = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state), name = "Hàng quản lý tạo ngay",
            baseUnitId = unitId, unitId, factor = 1, quantity = 3, categoryId, approveNow = true, barcode = "MANAGER-DIRECT-01" };
        state = await manager.JsonAsync(HttpMethod.Post, Url(documentId), direct);
        state = await manager.JsonAsync(HttpMethod.Post, Url(documentId), direct);
        Assert.Equal(0, state.GetProperty("unresolvedCount").GetInt32());
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId), new { commandId = Guid.NewGuid(),
            documentRowVersion = Version(state), name = "Dòng nhập nhầm", baseUnitId = unitId, unitId,
            factor = 1, quantity = 2, categoryId, barcode = "REMOVE-01" });
        var item = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("status").GetInt32() == 0);
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId) + $"/{item.GetProperty("id").GetInt32()}/remove", new {
            commandId = Guid.NewGuid(), documentRowVersion = Version(state), itemRowVersion = item.GetProperty("rowVersion").GetString() });
        Assert.Equal(0, state.GetProperty("unresolvedCount").GetInt32());
        using (var invalid = await employee.Http.PostAsJsonAsync(Url(documentId), new { commandId = Guid.NewGuid(),
            documentRowVersion = Version(state), name = "Quy đổi quá lớn", baseUnitId = unitId, unitName = "Thùng",
            factor = 999999999999999m, quantity = 999999999999999m })) Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await verify.Products.Where(x => x.Name == direct.name).ToListAsync());
        Assert.False((await verify.Products.SingleAsync(x => x.Name == direct.name)).IsSellable);
        Assert.Equal(3, (await verify.StockDocumentLines.SingleAsync(x => x.StockDocumentId == documentId)).BaseQuantity);
        Assert.False(await verify.Products.AnyAsync(x => x.Name == "Dòng nhập nhầm"));
        Assert.Equal(100, (await verify.InventoryBalances.SingleAsync()).OnHandQty);

        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId), new { commandId = Guid.NewGuid(),
            documentRowVersion = Version(state), name = "Nhận thêm rồi duyệt", baseUnitId = unitId, unitId,
            factor = 1, quantity = 2, barcode = "APPROVE-ADD-01" });
        var receiveAndApprove = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state),
            name = "Nhận thêm rồi duyệt", baseUnitId = unitId, unitId, factor = 1, quantity = 3,
            barcode = "APPROVE-ADD-01", categoryId, approveNow = true };
        await manager.JsonAsync(HttpMethod.Post, Url(documentId), receiveAndApprove);
        await manager.JsonAsync(HttpMethod.Post, Url(documentId), receiveAndApprove);
        var combinedHistory = (await manager.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("recentReceipts");
        Assert.Equal(receiveAndApprove.commandId, combinedHistory[0].GetProperty("commandId").GetGuid());
        Assert.Equal(3, combinedHistory[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(5, combinedHistory[0].GetProperty("currentQuantity").GetDecimal());
        Assert.Equal(combinedHistory[0].GetProperty("rowKey").GetString(), combinedHistory[1].GetProperty("rowKey").GetString());
    }

    [Fact]
    public async Task Intake_records_packing_before_catalog_creation_and_review_resolves_correct_physical_quantity()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Update,
            PermissionCodes.Inventory.StockDocument.Create));
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        int documentId, baseUnitId, conversionId, categoryId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            baseUnitId = variant.Product.BaseUnitId; categoryId = variant.Product.CategoryId;
            var baseUnit = await db.Units.SingleAsync(x => x.Id == baseUnitId); baseUnit.Name = "Hộp";
            var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                UnitId = baseUnitId, Factor = 1, IsBaseUnit = true, IsActive = true };
            db.ProductUnitConversions.Add(conversion);
            var document = new StockDocument { StoreId = store.StoreId, DocumentNo = "INTAKE-TEST", WarehouseId = store.WarehouseId,
                SupplierId = (await db.Suppliers.SingleAsync()).Id, ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Nhận hàng thực tế", IsMerchandisePaid = true };
            db.StockDocuments.Add(document); await db.SaveChangesAsync();
            documentId = document.Id; conversionId = conversion.Id;
        }
        var initial = await employee.JsonAsync(HttpMethod.Get, Url(documentId));
        Assert.True(initial.GetProperty("canCapture").GetBoolean());
        Assert.False(initial.GetProperty("canReview").GetBoolean());
        var state = initial.GetProperty("state");
        var known = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state), productUnitConversionId = conversionId,
            factor = 1, quantity = 3, barcode = "0001234567890" };
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId) + "/known", known);
        await employee.JsonAsync(HttpMethod.Post, Url(documentId) + "/known", known);
        var knownHistory = (await employee.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("recentReceipts");
        Assert.Equal(known.commandId, Assert.Single(knownHistory.EnumerateArray()).GetProperty("commandId").GetGuid());
        Assert.Equal(3, knownHistory[0].GetProperty("quantity").GetDecimal());
        Assert.StartsWith("line-", knownHistory[0].GetProperty("rowKey").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(3, (await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == documentId)).Quantity);
            Assert.Single(await db.ProductBarcodeVerificationRequests.Where(x => x.StockDocumentId == documentId).ToListAsync());
            Assert.False(await db.ProductVariantUnitBarcodes.AnyAsync(x => x.Barcode == known.barcode));
            Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty);
        }
        object Capture(decimal factor, bool approveNow = false) => new { commandId = Guid.NewGuid(), documentRowVersion = Version(state),
            name = "Sữa", productVariantId = store.VariantId, baseUnitId, unitName = "Thùng", factor, quantity = 2,
            barcode = "CARTON-0001", approveNow };
        using (var invalid = await employee.Http.PostAsJsonAsync(Url(documentId), Capture(0)))
            Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using (var denied = await employee.Http.PostAsJsonAsync(Url(documentId), Capture(24, true)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var otherStore = await foreign.Http.GetAsync(Url(documentId))) Assert.False(otherStore.IsSuccessStatusCode);
        var csrf = employee.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        employee.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await employee.Http.PostAsJsonAsync(Url(documentId), Capture(24)))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        employee.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        var capture = Capture(24);
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId), capture);
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId), capture);
        var pending = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("status").GetInt32() == 0);
        Assert.Equal(24, pending.GetProperty("proposedFactor").GetDecimal());
        Assert.Equal("Hộp", pending.GetProperty("proposedBaseUnitName").GetString());
        Assert.Equal(2, pending.GetProperty("quantity").GetDecimal());
        var pendingId = pending.GetProperty("id").GetInt32();
        var quantityRequest = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state),
            itemRowVersion = pending.GetProperty("rowVersion").GetString(), quantity = 3 };
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId) + $"/{pendingId}/quantity", quantityRequest);
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId) + $"/{pendingId}/quantity", quantityRequest);
        var afterEditHistory = (await employee.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("recentReceipts");
        Assert.Equal(2, afterEditHistory.GetArrayLength());
        Assert.Equal($"intake-{pendingId}", afterEditHistory[0].GetProperty("rowKey").GetString());
        Assert.Equal(2, afterEditHistory[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(3, afterEditHistory[0].GetProperty("currentQuantity").GetDecimal());
        pending = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == pendingId);
        Assert.Equal(3, pending.GetProperty("quantity").GetDecimal());
        using (var stale = await employee.Http.PostAsJsonAsync(Url(documentId) + $"/{pendingId}/quantity",
            new { commandId = Guid.NewGuid(), quantityRequest.documentRowVersion, quantityRequest.itemRowVersion, quantity = 4 }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var invalid = await employee.Http.PostAsJsonAsync(Url(documentId) + $"/{pendingId}/quantity",
            new { commandId = Guid.NewGuid(), documentRowVersion = Version(state), itemRowVersion = pending.GetProperty("rowVersion").GetString(), quantity = 999999999999999m }))
            Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using (var denied = await foreign.Http.PostAsJsonAsync(Url(documentId) + $"/{pendingId}/quantity", quantityRequest))
            Assert.False(denied.IsSuccessStatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.False(await db.Units.AnyAsync(x => x.Name == "Thùng"));
            Assert.Equal(1, await db.ProductUnitConversions.CountAsync());
            Assert.Single(await db.StockDocumentLines.Where(x => x.StockDocumentId == documentId).ToListAsync());
        }
        using (var conflict = await employee.Http.PostAsJsonAsync(Url(documentId), Capture(12)))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var newProduct = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state), name = "Sữa mới hoàn toàn",
            baseUnitId, unitName = "Thùng", factor = 12, quantity = 2, categoryId, barcode = "NEW-PRODUCT-001" };
        state = await employee.JsonAsync(HttpMethod.Post, Url(documentId), newProduct);
        var path = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "receipt-intake"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "warehouse.html"), await employee.Http.GetStringAsync($"/admin/warehouse-receiving/{documentId}"));
        await File.WriteAllTextAsync(Path.Combine(path, "manager-draft.html"), await manager.Http.GetStringAsync($"/admin/stock-documents/{documentId}"));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.False(await db.Products.AnyAsync(x => x.Name == newProduct.name));
            (await db.StockDocuments.SingleAsync(x => x.Id == documentId)).Status = StockDocumentStatus.PendingApproval;
            await db.SaveChangesAsync();
        }
        state = (await manager.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("state");
        foreach (var itemId in state.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("status").GetInt32() == 0).Select(x => x.GetProperty("id").GetInt32()).ToArray())
        {
            var item = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == itemId);
            var review = new { commandId = Guid.NewGuid(), documentRowVersion = Version(state), itemRowVersion = item.GetProperty("rowVersion").GetString(), approve = true, categoryId };
            using (var denied = await employee.Http.PostAsJsonAsync(Url(documentId) + $"/{itemId}/review", review)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            state = await manager.JsonAsync(HttpMethod.Post, Url(documentId) + $"/{itemId}/review", review);
            state = await manager.JsonAsync(HttpMethod.Post, Url(documentId) + $"/{itemId}/review", review);
        }
        Assert.Equal(0, state.GetProperty("unresolvedCount").GetInt32());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var lines = await db.StockDocumentLines.Where(x => x.StockDocumentId == documentId).ToListAsync();
            Assert.Contains(lines, x => x.ProductVariantId == store.VariantId && x.Factor == 24 && x.Quantity == 3 && x.BaseQuantity == 72);
            Assert.Contains(lines, x => x.ProductVariantId != store.VariantId && x.Factor == 12 && x.Quantity == 2 && x.BaseQuantity == 24);
            Assert.Equal(1, await db.Units.CountAsync(x => x.Name == "Thùng"));
            Assert.Equal(1, await db.Products.CountAsync(x => x.Name == newProduct.name));
            Assert.Equal(2, await db.ProductVariantUnitBarcodes.CountAsync(x => x.Barcode == "CARTON-0001" || x.Barcode == newProduct.barcode));
            Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty);
        }
        var reviewedHistory = (await manager.JsonAsync(HttpMethod.Get, Url(documentId))).GetProperty("recentReceipts");
        Assert.Equal(3, reviewedHistory.GetArrayLength()); // Reviewing/quantity edits are not new receipts.
        Assert.All(reviewedHistory.EnumerateArray(), entry => Assert.StartsWith("line-", entry.GetProperty("rowKey").GetString()));
    }
}

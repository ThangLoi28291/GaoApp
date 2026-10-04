using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptDocumentActionsSqlServerTests
{
    [Fact]
    public async Task Submitted_drafts_rejected_confirmed_and_purchase_receipts_keep_deletion_and_permission_boundaries()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var owner = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update,
            PermissionCodes.Inventory.StockDocument.Delete);
        using var employee = await app.LoginAsync(owner);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var purchaser = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Update,
            PermissionCodes.Purchase.Receipt.Delete));
        var url = $"/admin/api/stock-documents/{seed.ReceiptId}/document-actions";
        foreach (var status in new[] { StockDocumentStatus.Draft, StockDocumentStatus.Rejected, StockDocumentStatus.Confirmed })
        {
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
            {
                var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
                doc.Status = status; doc.SubmittedAtUtc = DateTime.UtcNow;
                doc.ConfirmedLegalEntityId = status == StockDocumentStatus.Confirmed
                    ? (await db.Warehouses.SingleAsync(x => x.Id == doc.WarehouseId)).LegalEntityId : null;
                await db.SaveChangesAsync();
            }
            var data = await employee.JsonAsync(HttpMethod.Get, url);
            Assert.False(data.GetProperty("canDelete").GetBoolean());
            Assert.False(data.GetProperty("canRename").GetBoolean());
            var body = new { title = "Tên quản lý", rowVersion = data.GetProperty("document").GetProperty("rowVersion").GetString() };
            foreach (var action in new[] { "rename", "delete", "request-title" })
            {
                using var denied = await employee.Http.PostAsJsonAsync(url + "/" + action, body);
                Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            }
            await manager.JsonAsync(HttpMethod.Post, url + "/rename", body);
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            Assert.Equal(StockDocumentStatus.Confirmed, doc.Status);
            var po = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "PO-TITLE-TEST", SupplierId = doc.SupplierId!.Value,
                ExpectedWarehouseId = doc.WarehouseId, LegalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == doc.WarehouseId)).LegalEntityId };
            db.Add(po); await db.SaveChangesAsync();
            doc.Status = StockDocumentStatus.Draft; doc.SubmittedAtUtc = null;
            doc.ConfirmedLegalEntityId = null;
            doc.ReceiptSource = PurchaseReceiptSource.PurchaseOrder; doc.PurchaseOrderId = po.Id;
            doc.ReceivingOwnerUserId = owner.UserId; doc.ReceivingLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        using (var denied = await employee.Http.GetAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var purchase = await purchaser.JsonAsync(HttpMethod.Get, url);
        var purchaseBody = new { title = "Tên phiếu theo đơn mua", rowVersion = purchase.GetProperty("document").GetProperty("rowVersion").GetString() };
        using (var denied = await employee.Http.PostAsJsonAsync(url + "/rename", purchaseBody)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var leased = await purchaser.Http.PostAsJsonAsync(url + "/delete", purchaseBody)) Assert.Equal(HttpStatusCode.Conflict, leased.StatusCode);
        await purchaser.JsonAsync(HttpMethod.Post, url + "/rename", purchaseBody);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.False((await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).IsDeleted);
    }

    [Fact]
    public async Task Rename_request_review_and_delete_enforce_state_permissions_versions_and_tenant_without_moving_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await using var baseline = app.Database.CreateTenantContext(store.StoreId);
        var beforeStock = await baseline.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost, x.AfterQty, x.AfterInventoryValue }).ToListAsync();
        var account = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update,
            PermissionCodes.Inventory.StockDocument.Delete, PermissionCodes.Inventory.StockDocument.Create);
        using var employee = await app.LoginAsync(account);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        string Url(int id) => $"/admin/api/stock-documents/{id}/document-actions";
        var url = Url(seed.ReceiptId);
        static string Version(JsonElement value) => value.GetProperty("document").GetProperty("rowVersion").GetString()!;
        using (var response = await viewer.Http.GetAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await foreign.Http.GetAsync(url)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var initial = await employee.JsonAsync(HttpMethod.Get, url);
        foreach (var title in new[] { "", "   ", new string('a', 256) })
        {
            using var response = await employee.Http.PostAsJsonAsync(url + "/rename", new { title, rowVersion = Version(initial) });
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        }
        await employee.JsonAsync(HttpMethod.Post, url + "/rename", new { title = "  Hàng nhập buổi sáng  ", rowVersion = Version(initial) });
        var renamed = await employee.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("Hàng nhập buổi sáng", renamed.GetProperty("document").GetProperty("documentTitle").GetString());
        Assert.NotEqual(Version(initial), Version(renamed));
        using (var stale = await employee.Http.PostAsJsonAsync(url + "/delete", new { rowVersion = Version(initial) })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var denied = await manager.Http.PostAsJsonAsync(url + "/delete", new { rowVersion = Version(renamed) })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            doc.Status = StockDocumentStatus.PendingApproval; doc.SubmittedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var pending = await employee.JsonAsync(HttpMethod.Get, url);
        Assert.False(pending.GetProperty("canRename").GetBoolean()); Assert.True(pending.GetProperty("canRequest").GetBoolean());
        foreach (var action in new[] { "rename", "delete" })
        {
            using var denied = await employee.Http.PostAsJsonAsync(url + "/" + action, new { title = "Không được ghi", rowVersion = Version(pending) });
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        }
        await employee.JsonAsync(HttpMethod.Post, url + "/request-title", new { title = "Tên nhân viên đề nghị", rowVersion = Version(pending) });
        var proposed = await manager.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("Hàng nhập buổi sáng", proposed.GetProperty("document").GetProperty("documentTitle").GetString());
        var proposalId = proposed.GetProperty("document").GetProperty("proposal").GetProperty("id").GetInt64();
        var decision = new { requestId = proposalId, rowVersion = Version(proposed), approve = true };
        using (var denied = await employee.Http.PostAsJsonAsync(url + "/review-title", decision)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var duplicate = await employee.Http.PostAsJsonAsync(url + "/request-title", new { title = "Yêu cầu trùng", rowVersion = Version(proposed) })) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await manager.JsonAsync(HttpMethod.Post, url + "/review-title", decision);
        using (var duplicate = await manager.Http.PostAsJsonAsync(url + "/review-title", decision)) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var approved = await employee.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("Tên nhân viên đề nghị", approved.GetProperty("document").GetProperty("documentTitle").GetString());
        Assert.Equal(2, approved.GetProperty("document").GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, approved.GetProperty("document").GetProperty("proposal").ValueKind);

        await employee.JsonAsync(HttpMethod.Post, url + "/request-title", new { title = "Tên bị từ chối", rowVersion = Version(approved) });
        var rejected = await manager.JsonAsync(HttpMethod.Get, url);
        await manager.JsonAsync(HttpMethod.Post, url + "/review-title", new { approve = false, rowVersion = Version(rejected),
            requestId = rejected.GetProperty("document").GetProperty("proposal").GetProperty("id").GetInt64() });
        var afterReject = await employee.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("Tên nhân viên đề nghị", afterReject.GetProperty("document").GetProperty("documentTitle").GetString());
        await employee.JsonAsync(HttpMethod.Post, url + "/request-title", new { title = "Tên chờ khác", rowVersion = Version(afterReject) });
        var beforeManager = await manager.JsonAsync(HttpMethod.Get, url);
        await manager.JsonAsync(HttpMethod.Post, url + "/rename", new { title = "Tên quản lý chốt", rowVersion = Version(beforeManager) });
        var final = await manager.JsonAsync(HttpMethod.Get, url);
        Assert.Equal(JsonValueKind.Null, final.GetProperty("document").GetProperty("proposal").ValueKind);

        int draftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            Assert.Equal(StockDocumentStatus.PendingApproval, doc.Status);
            Assert.All(doc.Lines, line => { Assert.Equal(2, line.Quantity); Assert.Equal(10, line.UnitCost); });
            var audit = await db.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == doc.Id).ToListAsync();
            Assert.Equal(3, audit.Count(x => x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeRequested));
            Assert.Single(audit, x => x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeApproved);
            Assert.Single(audit, x => x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeDeclined);
            var draft = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                DocumentNo = "DELETE-DRAFT", ReceiptSource = PurchaseReceiptSource.Direct,
                Lines = [new StockDocumentLine { ProductVariantId = store.VariantId, Quantity = 2, Factor = 1, BaseQuantity = 2,
                    UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 20, LineNo = 1, ProductNameSnapshot = "Hàng thử" }] };
            db.Add(draft); await db.SaveChangesAsync(); draftId = draft.Id;
        }
        var draftData = await employee.JsonAsync(HttpMethod.Get, Url(draftId));
        var token = employee.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        employee.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await employee.Http.PostAsJsonAsync(Url(draftId) + "/delete", new { rowVersion = Version(draftData) })) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        employee.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        // Competing changes using the same version cannot both commit.
        var races = await Task.WhenAll(employee.Http.PostAsJsonAsync(Url(draftId) + "/rename", new { title = "Phiên A", rowVersion = Version(draftData) }),
            employee.Http.PostAsJsonAsync(Url(draftId) + "/rename", new { title = "Phiên B", rowVersion = Version(draftData) }));
        Assert.Single(races, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(races, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in races) response.Dispose();
        draftData = await employee.JsonAsync(HttpMethod.Get, Url(draftId));
        await employee.JsonAsync(HttpMethod.Post, Url(draftId) + "/delete", new { rowVersion = Version(draftData) });
        using (var removed = await employee.Http.GetAsync(Url(draftId))) Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await check.StockDocuments.AnyAsync(x => x.Id == draftId));
        var deleted = await check.StockDocuments.IgnoreQueryFilters().SingleAsync(x => x.Id == draftId);
        Assert.True(deleted.IsDeleted); Assert.Equal(account.UserId, deleted.DeletedBy);
        Assert.True(await check.StockDocumentLines.IgnoreQueryFilters().AnyAsync(x => x.StockDocumentId == draftId));
        Assert.True(await check.PurchaseReceiptAuditEvents.AnyAsync(x => x.StockDocumentId == draftId && x.EventType == PurchaseReceiptAuditEventType.ReceiptDraftDeleted));
        Assert.Equal(beforeStock, await check.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost, x.AfterQty, x.AfterInventoryValue }).ToListAsync());
    }
}

using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptLineRemovalSqlServerTests
{
    [Theory]
    [InlineData(StockDocumentStatus.Draft)]
    [InlineData(StockDocumentStatus.Rejected)]
    public async Task Editable_receipt_can_remove_known_and_resolved_intake_lines_without_losing_evidence_or_stock(StockDocumentStatus status)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        await using (var setup = app.Database.CreateTenantContext(store.StoreId))
        {
            (await setup.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status = status;
            await setup.SaveChangesAsync();
        }
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Delete));
        foreach (var lineId in new[] { seed.KnownLineId, seed.ResolvedLineId })
            await employee.JsonAsync(HttpMethod.Delete, $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{lineId}");
        using var reader = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        var history = (await reader.JsonAsync(HttpMethod.Get, $"/admin/api/stock-documents/{seed.ReceiptId}/intake")).GetProperty("recentReceipts");
        Assert.Contains(history.EnumerateArray(), x => x.GetProperty("rowKey").GetString() == $"line-{seed.ResolvedLineId}" && x.GetProperty("isRemoved").GetBoolean());
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Empty(await db.StockDocumentLines.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync());
        Assert.Equal(2, await db.StockDocumentLines.IgnoreQueryFilters().CountAsync(x => (x.Id == seed.KnownLineId || x.Id == seed.ResolvedLineId) && x.IsDeleted));
        Assert.Equal(status, (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        Assert.Equal(0, (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).TotalAmount);
        var item = await db.StockDocumentProvisionalItems.SingleAsync(x => x.Id == seed.ItemId);
        Assert.Equal(seed.ResolvedLineId, item.ResolvedStockDocumentLineId);
        Assert.Equal(StockDocumentProvisionalItemStatus.Resolved, item.Status);
        Assert.Equal(2, await db.StockDocumentProvisionalItems.CountAsync(x => x.ResolvedStockDocumentLineId == seed.KnownLineId && x.Status == StockDocumentProvisionalItemStatus.Resolved));
        Assert.Equal(2, await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted));
        Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.True(await db.Products.AnyAsync(x => x.Id == seed.ProductId));
    }

    [Fact]
    public async Task Delete_checks_permission_csrf_tenant_document_identity_and_workflow_status()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var authorized = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Delete));
        using var readOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var url = $"/admin/api/stock-documents/{seed.ReceiptId}/lines/{seed.ResolvedLineId}";
        using (var denied = await readOnly.Http.DeleteAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.DeleteAsync(url)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var token = authorized.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        authorized.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await authorized.Http.DeleteAsync(url)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        authorized.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        int otherId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var other = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "WRONG-RECEIPT", ReceiptSource = PurchaseReceiptSource.Direct };
            db.Add(other); await db.SaveChangesAsync(); otherId = other.Id;
        }
        using (var wrong = await authorized.Http.DeleteAsync($"/admin/api/stock-documents/{otherId}/lines/{seed.ResolvedLineId}")) Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        foreach (var status in new[] { StockDocumentStatus.PendingApproval, StockDocumentStatus.Confirmed })
        {
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
            {
                var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
                receipt.Status = status;
                if (status == StockDocumentStatus.Confirmed)
                    receipt.ConfirmedLegalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId;
                await db.SaveChangesAsync();
            }
            using var denied = await authorized.Http.DeleteAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.True(await check.StockDocumentLines.AnyAsync(x => x.Id == seed.ResolvedLineId));
        Assert.Equal(seed.ResolvedLineId, (await check.StockDocumentProvisionalItems.SingleAsync(x => x.Id == seed.ItemId)).ResolvedStockDocumentLineId);
        Assert.False(await check.PurchaseReceiptAuditEvents.AnyAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted));
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }

    internal sealed record Seed(int ReceiptId, int KnownLineId, int ResolvedLineId, int ItemId, int ProductId);
    internal static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var basic = await ReceiptIntakeSupplierSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == basic.ReceiptId);
            doc.SupplierId = basic.SupplierId; await db.SaveChangesAsync();
        }
        var url = $"/admin/api/stock-documents/{basic.ReceiptId}/intake";
        var state = (await manager.JsonAsync(HttpMethod.Get, url)).GetProperty("state");
        var item = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == basic.ItemIds[0]);
        await manager.JsonAsync(HttpMethod.Post, url + $"/{basic.ItemIds[0]}/review", new {
            commandId = Guid.NewGuid(), documentRowVersion = state.GetProperty("documentRowVersion").GetString(),
            itemRowVersion = item.GetProperty("rowVersion").GetString(), categoryId = basic.CategoryId, approve = true });
        await using var setup = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await setup.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == basic.ReceiptId);
        receipt.Status = StockDocumentStatus.Rejected; receipt.ApprovalNote = "Nhập nhầm hàng";
        // Retain one known line and one catalog-resolved line, matching the reported screen.
        var known = receipt.Lines.Where(x => x.ProductVariantId == store.VariantId).OrderBy(x => x.Id).First();
        foreach (var extra in receipt.Lines.Where(x => x.ProductVariantId == store.VariantId && x.Id != known.Id)) extra.IsDeleted = true;
        var remaining = await setup.StockDocumentProvisionalItems.SingleAsync(x => x.Id == basic.ItemIds[1]);
        remaining.Status = StockDocumentProvisionalItemStatus.Removed; remaining.RemovedAtUtc = DateTime.UtcNow;
        var resolved = await setup.StockDocumentProvisionalItems.SingleAsync(x => x.Id == basic.ItemIds[0]);
        var variant = await setup.ProductVariants.SingleAsync(x => x.Id == resolved.ResolvedProductVariantId);
        await setup.SaveChangesAsync();
        state = (await manager.JsonAsync(HttpMethod.Get, url)).GetProperty("state");
        for (var i = 0; i < 2; i++)
            state = await manager.JsonAsync(HttpMethod.Post, url + "/known", new {
                commandId = Guid.NewGuid(), documentRowVersion = state.GetProperty("documentRowVersion").GetString(),
                productUnitConversionId = known.ProductUnitConversionId, factor = known.Factor, quantity = 1 });
        return new(receipt.Id, known.Id, resolved.ResolvedStockDocumentLineId!.Value, resolved.Id, variant.ProductId);
    }
}

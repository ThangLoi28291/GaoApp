using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptApprovalPerformanceSqlServerTests
{
    [Fact]
    public async Task Large_receipt_posts_inventory_payables_and_audit_once_and_rejects_foreign_access()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var product = await db.Products.SingleAsync();
        var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
        const int lines = 144;
        var variants = Enumerable.Range(1, lines).Select(i => new ProductVariant
        { StoreId = store.StoreId, ProductId = product.Id, Sku = $"APPROVAL-PERF-{i}", CostPrice = 10m, Price = 20m, IsActive = true }).ToArray();
        db.AddRange(variants); await db.SaveChangesAsync();
        var document = new StockDocument
        {
            StoreId = store.StoreId, WarehouseId = store.WarehouseId, SupplierId = supplierId,
            Type = StockDocumentType.Receipt, Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct, DirectReceiptReason = "Khác",
            DocumentNo = "APPROVAL-PERFORMANCE-144", DocumentDate = DateTime.UtcNow,
            SubtotalBeforeVat = lines * 20m, TotalAmount = lines * 20m,
            Lines = variants.Select((v, i) => new StockDocumentLine
            {
                ProductVariantId = v.Id, LineNo = i + 1,
                ProductNameSnapshot = product.Name, UnitId = product.BaseUnitId, UnitNameSnapshot = "Cái",
                Quantity = 2m, Factor = 1m, BaseQuantity = 2m,
                UnitPriceBeforeVat = 10m, UnitPriceAfterVat = 10m, UnitCost = 10m, LineTotal = 20m
            }).ToList()
        };
        db.StockDocuments.Add(document); await db.SaveChangesAsync();
        var payload = new
        {
            rowVersion = Convert.ToBase64String(document.RowVersion), supplierId,
            acceptPriceVariance = true, waitForInputInvoice = false,
            lines = document.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = 10m }).ToArray()
        };
        var url = $"/admin/api/stock-documents/{document.Id}/approve-commercial";
        using (var forbidden = await foreign.Http.PostAsJsonAsync(url, payload))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var watch = Stopwatch.StartNew();
        await admin.JsonAsync(HttpMethod.Post, url, payload);
        watch.Stop();
        db.ChangeTracker.Clear();
        var header = await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(StockDocumentStatus.Confirmed, header.Status);
        Assert.Equal(lines * 20m, header.TotalAmount);
        Assert.Equal(lines * 20m, (await db.PurchasePayables.SingleAsync()).Amount);
        var reference = document.Id.ToString();
        Assert.Equal(lines, await db.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == reference));
        Assert.Equal(lines, await db.InventoryCostLayers.CountAsync(x => x.ReferenceId == reference));
        Assert.Equal(lines, await db.InventoryValuationEntries.CountAsync(x => x.ReferenceId == reference));
        var variantIds = variants.Select(v => v.Id).ToArray();
        Assert.All(await db.InventoryBalances.Where(x => variantIds.Contains(x.ProductVariantId)).ToArrayAsync(),
            row => { Assert.Equal(2m, row.OnHandQty); Assert.Equal(20m, row.InventoryValue); });
        var auditCount = await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == document.Id);
        Assert.True(auditCount > 0);
        await admin.JsonAsync(HttpMethod.Post, url, payload);
        Assert.Equal(lines, await db.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == reference));
        Assert.Equal(1, await db.PurchasePayables.CountAsync());
        Assert.Equal(auditCount, await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == document.Id));
        var resultDirectory = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "receipt-approval-fix-20261029");
        Directory.CreateDirectory(resultDirectory);
        await File.WriteAllTextAsync(Path.Combine(resultDirectory, "approval-144-lines.json"),
            JsonSerializer.Serialize(new { lines, elapsedMs = watch.Elapsed.TotalMilliseconds, transactions = lines,
                repeatDidNotDuplicate = true, foreignAccessRejected = true, database = "Disposable test database" },
                new JsonSerializerOptions { WriteIndented = true }));
    }
}

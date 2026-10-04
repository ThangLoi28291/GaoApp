using System.Data.Common;
using System.Diagnostics;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptListSqlServerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Receipt_list_reads_only_summaries_and_preserves_counts_identity_and_tenant_scope()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        const int receiptCount = 1000;
        var date = new DateTime(2026, 9, 28);
        int emptyId, specialId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var supplier = await db.Suppliers.SingleAsync();
            var warehouse = await db.Warehouses.SingleAsync();
            var original = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            var second = new ProductVariant { StoreId = store.StoreId, ProductId = original.ProductId, Sku = "LIST-SECOND", IsActive = true };
            var po = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "LIST-PO", Title = "Đơn hàng thử danh sách", SupplierId = supplier.Id,
                ExpectedWarehouseId = warehouse.Id, LegalEntityId = warehouse.LegalEntityId };
            db.AddRange(second, po); await db.SaveChangesAsync();
            var receipts = Enumerable.Range(0, receiptCount).Select(i => new StockDocument
            {
                StoreId = store.StoreId, WarehouseId = store.WarehouseId, SupplierId = supplier.Id,
                DocumentNo = $"LIST-{i:D4}", DocumentDate = date, DocumentTitle = "Phiếu thử nhiều dòng",
                Status = i % 2 == 0 ? StockDocumentStatus.PendingApproval : StockDocumentStatus.Confirmed,
                ConfirmedLegalEntityId = i % 2 == 0 ? null : warehouse.LegalEntityId,
                TotalAmount = 123456, Note = new string('x', 900),
                Lines = Enumerable.Range(1, 20).Select(n => new StockDocumentLine
                {
                    LineNo = n, ProductVariantId = store.VariantId, ProductNameSnapshot = new string('x', 200),
                    Quantity = 1, BaseQuantity = 1, Factor = 1, UnitCost = 10, LineTotal = 10,
                    IsDeleted = n == 20
                }).ToList()
            }).ToList();
            receipts[0].PurchaseOrderId = po.Id;
            receipts[0].Lines.First().ProductVariantId = second.Id;
            receipts[0].HasRevisionRequest = true;
            receipts[0].RevisionRequestNote = "Kiểm tra số lượng";
            var empty = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "LIST-EMPTY", DocumentDate = date.AddDays(1) };
            db.AddRange(receipts); db.Add(empty);
            var deleted = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "LIST-DELETED" };
            db.Add(deleted);
            db.Add(new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "LIST-ISSUE", Type = StockDocumentType.Issue });
            await db.SaveChangesAsync();
            deleted.IsDeleted = true;
            foreach (var receipt in receipts) receipt.Lines.Last().IsDeleted = true;
            await db.SaveChangesAsync();
            emptyId = empty.Id; specialId = receipts[0].Id;
        }
        await using (var other = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            other.Add(new StockDocument { StoreId = app.Stores[1].StoreId, WarehouseId = app.Stores[1].WarehouseId, DocumentNo = "OTHER-TENANT" });
            await other.SaveChangesAsync();
        }
        var capture = new ListReadCapture();
        await using (var db = app.Database.CreateTenantContext(store.StoreId, capture))
        {
            var timer = Stopwatch.StartNew();
            var items = await new StockDocumentRepository(db).GetReceiptListAsync();
            output.WriteLine($"SQL: {items.Count} receipt summaries / 20,000 historical lines in {timer.ElapsedMilliseconds} ms.");
            Assert.Equal(receiptCount + 1, items.Count);
            Assert.Equal(emptyId, items[0].Id);
            Assert.Equal(0, items[0].TotalLines);
            Assert.Equal(0, items[0].TotalProductTypes);
            Assert.Null(items[0].SupplierName);
            Assert.Equal(items.Skip(1).Select(x => x.Id).OrderDescending(), items.Skip(1).Select(x => x.Id));
            var special = Assert.Single(items, x => x.Id == specialId);
            Assert.Equal(19, special.TotalLines);
            Assert.Equal(2, special.TotalProductTypes);
            Assert.Equal("LIST-PO", special.PurchaseOrderNumber);
            Assert.Equal("Đơn hàng thử danh sách", special.PurchaseOrderTitle);
            Assert.True(special.HasRevisionRequest);
            Assert.Equal("Kiểm tra số lượng", special.RevisionRequestNote);
            Assert.Equal(123456, special.TotalAmount);
            Assert.False(string.IsNullOrWhiteSpace(special.SupplierName));
            Assert.False(string.IsNullOrWhiteSpace(special.LegalEntityName));
            Assert.All(items.Where(x => x.Id != emptyId && x.Id != specialId), item =>
            {
                Assert.Equal(19, item.TotalLines); Assert.Equal(1, item.TotalProductTypes);
            });
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(1, capture.Commands);
            Assert.DoesNotContain("ProductNameSnapshot", capture.Sql);
            Assert.DoesNotContain("UnitCost", capture.Sql);
            Assert.Contains("COUNT", capture.Sql);
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var endpointTimer = Stopwatch.StartNew();
        var json = await manager.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        Assert.Equal(receiptCount + 1, json.GetArrayLength());
        output.WriteLine($"HTTP: {json.GetArrayLength()} summaries in {endpointTimer.ElapsedMilliseconds} ms.");
    }

    private sealed class ListReadCapture : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public string Sql { get; private set; } = "";
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++; Sql = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}

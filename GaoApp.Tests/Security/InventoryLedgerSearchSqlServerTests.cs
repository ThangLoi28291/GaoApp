using System.Diagnostics;
using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class InventoryLedgerSearchSqlServerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Xml_reference_search_and_quick_view_preserve_receipt_identity_paging_tenant_and_active_links()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var otherStore = app.Stores[1];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var foreignSeed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, otherStore);
        var reference = seed.ReceiptId.ToString();
        int transactionId;
        int mapId;
        const string documentNo = "NK-LEDGER-XML-TEST";
        const string xmlNumber = "00006598";
        await using (var db = app.Database.CreateHostContext())
        {
            var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            receipt.DocumentNo = documentNo;
            receipt.Status = StockDocumentStatus.Confirmed;
            receipt.ConfirmedLegalEntityId = await db.Warehouses.Where(x => x.Id == receipt.WarehouseId)
                .Select(x => x.LegalEntityId).SingleAsync();
            InputInvoiceHead Head(int storeId) => new()
            {
                StoreId = storeId, SellerName = "Supplier", SellerTaxCode = "0312770607",
                BuyerTaxCode = "0101234567", InvoiceSeries = "C26MYY", InvoiceNumber = xmlNumber,
                InvoiceDate = new DateTime(2026, 6, 23)
            };
            var head = Head(store.StoreId);
            var foreignHead = Head(otherStore.StoreId);
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head);
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(foreignHead);
            db.AddRange(head, foreignHead);
            await db.SaveChangesAsync();
            var link = new StockDocumentInputInvoiceMap
                { StoreId = store.StoreId, StockDocumentId = seed.ReceiptId, InputInvoiceHeadId = head.Id };
            db.Add(link);
            db.Add(new StockDocumentInputInvoiceMap
                { StoreId = otherStore.StoreId, StockDocumentId = foreignSeed.ReceiptId, InputInvoiceHeadId = foreignHead.Id });
            for (var i = 0; i < 23; i++)
                db.Add(new InventoryTransaction
                {
                    StoreId = store.StoreId, WarehouseId = store.WarehouseId, ProductVariantId = store.VariantId,
                    ReferenceType = InventoryReferenceType.StockDocument, ReferenceId = reference,
                    ReferenceLineId = i + 1, TransactionType = InventoryTransactionType.PurchaseReceipt,
                    QuantityChange = 1m, BeforeQty = i - 5, AfterQty = i - 4,
                    OccurredAtUtc = new DateTime(2026, 6, 23).AddMinutes(i),
                    Note = i == 0 ? "XML " + xmlNumber : "Nhập hàng"
                });
            // The same numeric ID on an order is not the receipt's XML reference.
            db.Add(new InventoryTransaction
            {
                StoreId = store.StoreId, WarehouseId = store.WarehouseId, ProductVariantId = store.VariantId,
                ReferenceType = InventoryReferenceType.Order, ReferenceId = reference,
                TransactionType = InventoryTransactionType.SaleIssue, QuantityChange = -1,
                OccurredAtUtc = new DateTime(2026, 6, 23)
            });
            db.Add(new InventoryTransaction
            {
                StoreId = otherStore.StoreId, WarehouseId = otherStore.WarehouseId, ProductVariantId = otherStore.VariantId,
                ReferenceType = InventoryReferenceType.StockDocument, ReferenceId = foreignSeed.ReceiptId.ToString(),
                TransactionType = InventoryTransactionType.PurchaseReceipt, QuantityChange = 1,
                OccurredAtUtc = new DateTime(2026, 6, 23)
            });
            await db.SaveChangesAsync();
            mapId = link.Id;
            transactionId = await db.InventoryTransactions.Where(x => x.StoreId == store.StoreId &&
                x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == reference)
                .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        }
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View));
        foreach (var search in new[] { xmlNumber, "6598", "C26MYY", "c26myy", "C26MYY · 00006598", documentNo })
        {
            var page = await viewer.JsonAsync(HttpMethod.Get,
                "/admin/inventory-ledger/data?searchScope=reference&keyword=" + Uri.EscapeDataString(search));
            Assert.Equal(23, page.GetProperty("totalItems").GetInt32());
            Assert.Equal(23, page.GetProperty("summary").GetProperty("increaseItems").GetInt32());
            Assert.Equal(20, page.GetProperty("items").GetArrayLength());
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(reference, item.GetProperty("referenceCode").GetString());
                Assert.Equal(documentNo, item.GetProperty("receiptDocumentNo").GetString());
                var xml = Assert.Single(item.GetProperty("linkedInputInvoices").EnumerateArray());
                Assert.Equal("C26MYY", xml.GetProperty("invoiceSeries").GetString());
                Assert.Equal(xmlNumber, xml.GetProperty("invoiceNumber").GetString());
            }
        }
        var lastPage = await viewer.JsonAsync(HttpMethod.Get,
            "/admin/inventory-ledger/data?searchScope=reference&keyword=" + xmlNumber + "&page=2&includeSummary=false");
        Assert.Equal(23, lastPage.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, lastPage.GetProperty("items").GetArrayLength());
        var all = await viewer.JsonAsync(HttpMethod.Get, "/admin/inventory-ledger/data?searchScope=all&keyword=" + xmlNumber);
        Assert.Equal(23, all.GetProperty("totalItems").GetInt32());
        var filtered = await viewer.JsonAsync(HttpMethod.Get, "/admin/inventory-ledger/data?referenceCode=" + xmlNumber);
        Assert.Equal(23, filtered.GetProperty("totalItems").GetInt32());
        var quick = (await viewer.JsonAsync(HttpMethod.Get,
            $"/admin/inventory-ledger/quick-view?transactionId={transactionId}")).GetProperty("item");
        Assert.Equal(documentNo, quick.GetProperty("receiptDocumentNo").GetString());
        Assert.Equal(xmlNumber, Assert.Single(quick.GetProperty("linkedInputInvoices").EnumerateArray())
            .GetProperty("invoiceNumber").GetString());
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(otherStore, PermissionCodes.Inventory.Transaction.View));
        var foreignPage = await foreign.JsonAsync(HttpMethod.Get,
            "/admin/inventory-ledger/data?searchScope=reference&keyword=" + xmlNumber);
        Assert.Equal(1, foreignPage.GetProperty("totalItems").GetInt32());
        Assert.Equal(foreignSeed.ReceiptId.ToString(), Assert.Single(foreignPage.GetProperty("items").EnumerateArray())
            .GetProperty("referenceCode").GetString());
        using (var response = await foreign.Http.GetAsync($"/admin/inventory-ledger/quick-view?transactionId={transactionId}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var context = app.Database.CreateTenantContext(store.StoreId);
        var before = await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.BeforeQty, x.AfterQty, x.TotalCost, Version = Convert.ToBase64String(x.RowVersion) }).ToListAsync();
        var linkToDelete = await context.StockDocumentInputInvoiceMaps.SingleAsync(x => x.Id == mapId);
        linkToDelete.IsDeleted = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repo = new InventoryLedgerIndexReadRepository(context);
        var afterUnlink = await repo.QueryAsync(store.StoreId, new() { SearchScope = "reference", Keyword = xmlNumber }, null);
        Assert.Empty(afterUnlink.Items);
        var unlinked = await repo.GetQuickViewAsync(store.StoreId, transactionId, null);
        Assert.Empty(unlinked!.Item.LinkedInputInvoices);
        Assert.Equal(documentNo, unlinked.Item.ReceiptDocumentNo);
        Assert.Equal(before, await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.BeforeQty, x.AfterQty, x.TotalCost, Version = Convert.ToBase64String(x.RowVersion) }).ToListAsync());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    internal static async Task<int> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int backgroundCount)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var source = await db.Products.SingleAsync();
        var milk = new Product { StoreId = store.StoreId, CategoryId = source.CategoryId,
            BaseUnitId = source.BaseUnitId, SupplierId = source.SupplierId, Name = "Sữa tươi Đà Lạt", Alias = "milk-ledger",
            IsActive = false, Variants = [
                new ProductVariant { StoreId = store.StoreId, Sku = "MILK-LEDGER", IsActive = false },
                new ProductVariant { StoreId = store.StoreId, Sku = "MILK-NO-MOVEMENT", IsActive = false }] };
        db.Add(milk); await db.SaveChangesAsync();
        var milkId = milk.Variants.Single(v => v.Sku == "MILK-LEDGER").Id;
        db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = milkId, UnitId = source.BaseUnitId,
            IsBaseUnit = true, Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8938505974194" }] });
        for (int i = 0; i < 45; i++) db.Add(new InventoryTransaction { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
            ProductVariantId = milkId, QuantityChange = i % 2 == 0 ? 1 : -1, BeforeQty = 0, AfterQty = i % 2 == 0 ? 1 : -1,
            TransactionType = InventoryTransactionType.PurchaseReceipt, ReferenceType = InventoryReferenceType.StockDocument,
            ReferenceId = "MILK-DOC-" + i, Note = i == 0 ? "Sữa tươi trùng cả ghi chú" : "Nhận hàng",
            OccurredAtUtc = new DateTime(2026, 9, 30).AddMinutes(i) });
        db.Add(new InventoryTransaction { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
            ProductVariantId = store.VariantId, QuantityChange = 2, BeforeQty = 0, AfterQty = 2,
            TransactionType = InventoryTransactionType.AdjustmentIncrease, ReferenceType = InventoryReferenceType.Adjustment,
            ReferenceId = "NOTE-ONLY", Note = "Sữa tươi đối soát", OccurredAtUtc = new DateTime(2026, 9, 29) });
        await db.SaveChangesAsync();
        if (backgroundCount > 0)
        {
            db.Database.SetCommandTimeout(180);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO InventoryTransactions (StoreId, WarehouseId, ProductVariantId, TransactionType, ReferenceType,
                    ReferenceId, QuantityChange, BeforeQty, AfterQty, CostSourceType, OccurredAtUtc, CreatedAtUtc, IsDeleted, Note)
                SELECT TOP ({backgroundCount}) {store.StoreId}, {store.WarehouseId}, {store.VariantId}, 20, 1,
                    CONCAT('BG-', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), -1, 10, 9, 0,
                    DATEADD(second, -CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), '2026-09-28'),
                    SYSUTCDATETIME(), 0, N'Xuất bán hàng tại quầy thu ngân'
                FROM sys.all_objects a CROSS JOIN sys.all_objects b;
                """);
        }
        return milkId;
    }

    [Fact]
    public async Task Search_large_ledger_preserves_accent_alias_note_union_counts_paging_filters_and_tenant()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var initialCount = await db.InventoryTransactions.CountAsync();
        await SeedAsync(app, store, 1_500_000);
        var repo = new InventoryLedgerIndexReadRepository(db);
        var periodClock = Stopwatch.StartNew();
        var period = await repo.GetPeriodAsync(store.StoreId, store.WarehouseId, store.VariantId, null, null);
        var periodMs = periodClock.ElapsedMilliseconds;
        Assert.NotNull(period);
        Assert.Equal(0m, period.OpeningQty);
        Assert.Equal(102m, period.IncreaseQty);
        Assert.Equal(1_500_000m, period.DecreaseQty);
        Assert.Equal(-1_499_898m, period.ClosingQty);
        output.WriteLine($"Full-history stock period across 1,500,047 movements: {periodMs} ms");
        Assert.True(periodMs < 20_000, $"Full stock period exceeded the phone request timeout: {periodMs}ms");
        async Task<InventoryLedgerIndexPageDto> Search(string keyword, bool summary = true, int page = 1, string? scope = null)
        {
            var clock = Stopwatch.StartNew();
            var result = await repo.QueryAsync(store.StoreId, new() { Keyword = keyword, SearchScope = scope, IncludeSummary = summary, Page = page, PageSize = 20 }, null);
            output.WriteLine($"Search '{keyword}', scope={scope ?? "all"}, summary={summary}: {clock.ElapsedMilliseconds} ms, {result.TotalItems} matches");
            return result;
        }
        Assert.Equal(initialCount + 1_500_046, (await Search("", scope: "product")).TotalItems);
        var productOnly = await Search("sua tuoi", scope: "product");
        Assert.Equal(45, productOnly.TotalItems);
        Assert.Equal(23, productOnly.Summary.IncreaseItems);
        Assert.Equal(45, (await Search("SỮA TƯƠI", scope: "product")).TotalItems);
        Assert.Equal(45, (await Search("da lat", scope: "product")).TotalItems);
        Assert.Equal(45, (await Search("8938505974194", scope: "product")).TotalItems);
        var productLastPage = await Search("sua tuoi", false, 3, "product");
        Assert.Equal(5, productLastPage.Items.Count);
        Assert.Empty(productOnly.Items.Select(x => x.TransactionId).Intersect(productLastPage.Items.Select(x => x.TransactionId)));
        Assert.Empty((await Search("no-such-unique-ledger-value", scope: "product")).Items);
        Assert.Equal(1, (await Search("NOTE-ONLY", scope: "reference")).TotalItems);
        Assert.Equal(1, (await Search("doi soat", scope: "note")).TotalItems);
        var milk = await Search("sua tuoi");
        Assert.Equal(46, milk.TotalItems); Assert.Equal(24, milk.Summary.IncreaseItems); Assert.Equal(22, milk.Summary.DecreaseItems);
        Assert.Equal(22, milk.Summary.NegativeItems); Assert.Equal(20, milk.Items.Count);
        Assert.Equal(46, (await Search("SỮA TƯƠI")).TotalItems);
        Assert.Equal(45, (await Search("da lat")).TotalItems);
        Assert.Equal(45, (await Search("MILK-LEDGER")).TotalItems);
        Assert.Equal(45, (await Search("8938505974194")).TotalItems);
        Assert.Equal(1, (await Search("doi soat")).TotalItems);
        Assert.Equal(1, (await Search("NOTE-ONLY")).TotalItems);
        var second = await Search("sua tuoi", false, 2);
        Assert.Equal(46, second.TotalItems); Assert.Empty(milk.Items.Select(x => x.TransactionId).Intersect(second.Items.Select(x => x.TransactionId)));
        Assert.Equal(6, (await Search("sua tuoi", false, 3)).Items.Count);
        Assert.Empty((await Search("no-such-unique-ledger-value")).Items);
        var negative = await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", State = "negative", PageSize = 100 }, null);
        Assert.Equal(22, negative.TotalItems); Assert.All(negative.Items, x => Assert.True(x.AfterQty < 0));
        var dated = await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", FromDate = new(2026, 9, 30), PageSize = 100 }, null);
        Assert.Equal(45, dated.TotalItems);
        Assert.Empty((await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", WarehouseId = app.Stores[1].WarehouseId }, null)).Items);
        await using var otherDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        Assert.Empty((await new InventoryLedgerIndexReadRepository(otherDb).QueryAsync(app.Stores[1].StoreId, new() { Keyword = "sua tuoi" }, null)).Items);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}

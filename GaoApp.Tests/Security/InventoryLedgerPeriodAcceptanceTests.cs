using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryLedgerPeriodAcceptanceTests
{
    [Fact]
    public async Task Full_ledger_period_and_verified_sources_preserve_business_dates_tenants_rights_and_desktop()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View);
        var stockViewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View, PermissionCodes.Inventory.StockDocument.View);
        var purchaseViewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View, PermissionCodes.Purchase.Receipt.View);
        var denied = await app.AddAccountAsync(store, PermissionCodes.Inventory.Balance.View);
        var foreign = await app.AddAccountAsync(app.Stores[1], "*");
        var receipt = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var foreignReceipt = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, app.Stores[1]);
        var expected = new Dictionary<int, string>();
        var unsupported = new List<int>();
        int secondWarehouseId, noHistoryWarehouseId, receiptTransactionId, crossTransactionId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var initial = await db.InventoryTransactions.SingleAsync(x => x.StoreId == store.StoreId);
            initial.OccurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var legalId = await db.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var second = new Warehouse { StoreId = store.StoreId, LegalEntityId = legalId, Code = "PERIOD-2", Name = "Kho thứ hai", IsActive = true };
            var empty = new Warehouse { StoreId = store.StoreId, LegalEntityId = legalId, Code = "PERIOD-EMPTY", Name = "Kho chưa ghi sổ", IsActive = true };
            db.AddRange(second, empty); await db.SaveChangesAsync();
            secondWarehouseId = second.Id; noHistoryWarehouseId = empty.Id;
            var count = new StockCountDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "KK-PERIOD" };
            var transfer = new StockTransferDocument { StoreId = store.StoreId, FromWarehouseId = store.WarehouseId,
                ToWarehouseId = second.Id, DocumentNo = "CK-PERIOD", DocumentDate = DateTime.UtcNow };
            var adjustment = new InventoryAdjustmentDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                DocumentNo = "DC-PERIOD", AdjustmentType = InventoryTransactionType.AdjustmentIncrease };
            var shift = new POSShift { StoreId = store.StoreId, WarehouseId = store.WarehouseId, TerminalId = store.TerminalId,
                OpenedByUserId = manager.UserId, ShiftCode = "PERIOD-SHIFT", Status = POSShiftStatus.Open, OpenedAtUtc = DateTime.UtcNow };
            db.AddRange(count, transfer, adjustment, shift); await db.SaveChangesAsync();
            var order = new Order { StoreId = store.StoreId, POSShiftId = shift.Id, OrderNumber = "PERIOD-ORDER",
                Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid, CreatedBy = manager.UserId };
            db.Add(order); await db.SaveChangesAsync();
            var refund = new SalesReturn { StoreId = store.StoreId, OrderId = order.Id, POSShiftId = shift.Id,
                ReturnNumber = "PERIOD-REFUND", Reason = "Test source" };
            var delivery = new GaoApp.Domain.Delivery.DeliveryOrder { StoreId = store.StoreId, Code = "PERIOD-DELIVERY",
                LookupToken = Guid.NewGuid().ToString("N"), SourceWarehouseId = store.WarehouseId, SourceLegalEntityId = legalId,
                SourceCartId = order.Id, CreatedTerminalId = store.TerminalId, CreatedShiftId = shift.Id,
                CreatedByUserId = manager.UserId, QuotedTotal = 10, RecipientName = "Period", RecipientPhone = "0901234567", RecipientAddress = "Fixture" };
            db.AddRange(refund, delivery); await db.SaveChangesAsync();
            InventoryTransaction Row(decimal qty, DateTime date, InventoryReferenceType type = InventoryReferenceType.None, string? reference = null,
                int? warehouseId = null, decimal before = 999m) => new()
            {
                StoreId = store.StoreId, WarehouseId = warehouseId ?? store.WarehouseId, ProductVariantId = store.VariantId,
                TransactionType = qty >= 0 ? InventoryTransactionType.AdjustmentIncrease : InventoryTransactionType.AdjustmentDecrease,
                ReferenceType = type, ReferenceId = reference ?? "", QuantityChange = qty, BeforeQty = before, AfterQty = before + qty,
                OccurredAtUtc = date, Note = "period fixture"
            };
            var boundary = new DateTime(2026, 1, 9, 17, 0, 0, DateTimeKind.Utc); // Vietnam Jan 10 00:00.
            db.Add(Row(10, boundary));
            db.Add(Row(50, boundary.AddDays(1))); // Exclusive end for Jan 10.
            db.Add(Row(-3, boundary)); // Identical business timestamp, later posting identity.
            db.Add(Row(7, boundary.AddSeconds(-1))); // Backdated posting must affect opening.
            db.Add(Row(-5, boundary.AddDays(2)));
            db.Add(Row(1, boundary, warehouseId: second.Id, before: 20)); // Independent baseline and warehouse.
            var sources = new[] {
                (InventoryReferenceType.StockDocument, receipt.ReceiptId, $"/admin/stock-documents/{receipt.ReceiptId}"),
                (InventoryReferenceType.Order, order.Id, $"/admin/pos/order-detail/{order.Id}"),
                (InventoryReferenceType.Refund, refund.Id, $"/admin/pos/order-detail/{order.Id}"),
                (InventoryReferenceType.StockCount, count.Id, $"/admin/stock-counts/{count.Id}"),
                (InventoryReferenceType.StockTransfer, transfer.Id, $"/admin/stock-transfers/{transfer.Id}"),
                (InventoryReferenceType.Adjustment, adjustment.Id, $"/admin/inventory-adjustment-documents/{adjustment.Id}"),
                (InventoryReferenceType.Delivery, delivery.Id, $"/admin/deliveries/{delivery.Id}/bill")
            };
            var sourceRows = sources.Select(x => Row(0, boundary, x.Item1, x.Item2.ToString())).ToArray();
            db.AddRange(sourceRows);
            var invalidRows = new[] {
                Row(0, boundary, InventoryReferenceType.StockDocument, foreignReceipt.ReceiptId.ToString()),
                Row(0, boundary, InventoryReferenceType.Order, "https://attacker.invalid/"),
                Row(0, boundary, InventoryReferenceType.StockDocument, "NK-PERIOD-123"),
                Row(0, boundary, InventoryReferenceType.PurchaseReturn, order.Id.ToString())
            };
            db.AddRange(invalidRows); await db.SaveChangesAsync();
            for (var i = 0; i < sources.Length; i++) expected[sourceRows[i].Id] = sources[i].Item3;
            unsupported.AddRange(invalidRows.Select(x => x.Id)); receiptTransactionId = sourceRows[0].Id;
        }
        await using (var db = app.Database.CreateHostContext())
            crossTransactionId = await db.InventoryTransactions.Where(x => x.StoreId == app.Stores[1].StoreId).Select(x => x.Id).FirstAsync();

        using var admin = await app.LoginAsync(manager);
        using var readOnly = await app.LoginAsync(viewer);
        using var noRights = await app.LoginAsync(denied);
        using var other = await app.LoginAsync(foreign);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); }
        string Period(int warehouseId, int variantId, string dates = "") =>
            $"/admin/inventory-ledger/period?warehouseId={warehouseId}&productVariantId={variantId}{dates}";
        var path = Period(store.WarehouseId, store.VariantId, "&fromDate=2026-01-10&toDate=2026-01-10");
        var protectedBefore = await ProtectedAsync(app);
        var summary = await readOnly.JsonAsync(HttpMethod.Get, path + "&page=2&pageSize=10&state=increase&transactionType=10&keyword=does-not-match&includeCost=true");
        AssertPeriod(summary, 107, 10, 3, 114);
        Assert.Equal(summary.GetRawText(), (await readOnly.JsonAsync(HttpMethod.Get, path)).GetRawText());
        AssertPeriod(await readOnly.JsonAsync(HttpMethod.Get, Period(store.WarehouseId, store.VariantId,
            "&fromDate=2026-01-12&toDate=2026-01-10")), 107, 60, 8, 159); // Reversed range normalized.
        AssertPeriod(await admin.JsonAsync(HttpMethod.Get, Period(store.WarehouseId, store.VariantId,
            "&fromDate=2026-01-15&toDate=2026-01-16")), 159, 0, 0, 159);
        AssertPeriod(await admin.JsonAsync(HttpMethod.Get, Period(store.WarehouseId, store.VariantId)), 0, 167, 8, 159);
        AssertPeriod(await admin.JsonAsync(HttpMethod.Get, Period(secondWarehouseId, store.VariantId,
            "&fromDate=2026-01-10&toDate=2026-01-10")), 20, 1, 0, 21);
        var noHistory = await admin.JsonAsync(HttpMethod.Get, Period(noHistoryWarehouseId, store.VariantId));
        Assert.False(noHistory.GetProperty("hasRecordedHistory").GetBoolean());
        Assert.Equal(JsonValueKind.Null, noHistory.GetProperty("openingQty").ValueKind);
        Assert.Equal(JsonValueKind.Null, noHistory.GetProperty("closingQty").ValueKind);
        foreach (var bad in new[] { "&fromDate=0001-01-01", "&toDate=9999-12-31", "&fromDate=bad" })
        { using var response = await admin.Http.GetAsync(Period(store.WarehouseId, store.VariantId, bad)); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        foreach (var crossPath in new[] { Period(app.Stores[1].WarehouseId, store.VariantId), Period(store.WarehouseId, app.Stores[1].VariantId),
            $"/admin/inventory-ledger/source?transactionId={crossTransactionId}" })
        { using var response = await admin.Http.GetAsync(crossPath); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        using (var response = await other.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await noRights.Http.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await admin.Http.GetAsync(path)) Assert.True(response.Headers.CacheControl?.NoStore);
        foreach (var (id, target) in expected)
        {
            var detail = (await admin.JsonAsync(HttpMethod.Get, $"/admin/inventory-ledger/quick-view?transactionId={id}")).GetProperty("item");
            var sourceUrl = detail.GetProperty("sourceUrl").GetString()!;
            using var response = await admin.Http.GetAsync(sourceUrl);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal(target, response.Headers.Location?.OriginalString);
            var restricted = (await readOnly.JsonAsync(HttpMethod.Get, $"/admin/inventory-ledger/quick-view?transactionId={id}")).GetProperty("item");
            Assert.False(restricted.TryGetProperty("sourceUrl", out _));
            Assert.False(restricted.TryGetProperty("sourceId", out _));
            using var blocked = await readOnly.Http.GetAsync(sourceUrl); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        }
        foreach (var id in unsupported)
        {
            var detail = (await admin.JsonAsync(HttpMethod.Get, $"/admin/inventory-ledger/quick-view?transactionId={id}")).GetProperty("item");
            Assert.False(detail.TryGetProperty("sourceUrl", out _));
            using var response = await admin.Http.GetAsync($"/admin/inventory-ledger/source?transactionId={id}"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        var listed = await admin.JsonAsync(HttpMethod.Get, "/admin/inventory-ledger/data?pageSize=10");
        Assert.True(listed.GetProperty("totalItems").GetInt32() > 10);
        Assert.All(listed.GetProperty("items").EnumerateArray(), item => Assert.True(item.GetProperty("warehouseId").GetInt32() > 0));
        Assert.Equal(protectedBefore, await ProtectedAsync(app));

        var output = Environment.GetEnvironmentVariable("GAO_LEDGER_PERIOD_REVIEW_OUTPUT")
            ?? Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "mobile-ui-24-ledger-20261029");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "period-api.json"), summary.GetRawText());
        await BrowserAsync(app, manager, viewer, store, receiptTransactionId, output);
        Assert.Equal(protectedBefore, await ProtectedAsync(app));
        using var stockOnly = await app.LoginAsync(stockViewer);
        using var purchaseOnly = await app.LoginAsync(purchaseViewer);
        var receiptSourcePath = $"/admin/inventory-ledger/source?transactionId={receiptTransactionId}";
        using (var response = await stockOnly.Http.GetAsync(receiptSourcePath)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await purchaseOnly.Http.GetAsync(receiptSourcePath)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.StockDocuments.SingleAsync(x => x.Id == receipt.ReceiptId)).ReceiptSource = PurchaseReceiptSource.PurchaseOrder; await db.SaveChangesAsync(); }
        using (var response = await stockOnly.Http.GetAsync(receiptSourcePath)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await purchaseOnly.Http.GetAsync(receiptSourcePath)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Source existence and permissions are checked again at click time, not trusted from an old response.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.StockDocuments.SingleAsync(x => x.Id == receipt.ReceiptId)).IsDeleted = true; await db.SaveChangesAsync(); }
        using (var response = await admin.Http.GetAsync($"/admin/inventory-ledger/source?transactionId={receiptTransactionId}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, actualMigrations = true, businessDateVietnamBoundaries = true, backdatedPosting = true,
            emptyPeriod = true, separateWarehouseBaseline = true, noHistoryIsUnknown = true, noMixedUnitTotal = true,
            typedSources = expected.Count, invalidSources = unsupported.Count, clickRevalidation = true, purchaseAndStockReceiptPoliciesDistinct = true,
            noReadWrites = true, storesMenusRightsSettingsStockVouchersUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AssertPeriod(JsonElement value, decimal opening, decimal increase, decimal decrease, decimal closing)
    {
        Assert.True(value.GetProperty("hasRecordedHistory").GetBoolean());
        Assert.Equal(opening, value.GetProperty("openingQty").GetDecimal());
        Assert.Equal(increase, value.GetProperty("increaseQty").GetDecimal());
        Assert.Equal(decrease, value.GetProperty("decreaseQty").GetDecimal());
        Assert.Equal(closing, value.GetProperty("closingQty").GetDecimal());
        Assert.DoesNotContain("cost", value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ProtectedAsync(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<T>() where T : class => await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync();
        // Scalar properties avoid navigation cycles and retain every persisted column.
        async Task<object> Scalars<T>() where T : class
        {
            var rows = (IEnumerable<T>)await Rows<T>();
            return rows.Select(row => db.Entry(row).Properties.OrderBy(p => p.Metadata.Name)
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)).ToArray();
        }
        return JsonSerializer.Serialize(new {
            stores = await Scalars<Store>(), menus = await Scalars<AdminMenuItem>(), roles = await Scalars<Role>(),
            grants = await Scalars<RolePermission>(), stock = await Scalars<InventoryBalance>(), ledger = await Scalars<InventoryTransaction>(),
            settings = await Scalars<RewardSettings>(), vouchers = await Scalars<CustomerRewardVoucher>()
        });
    }

    private static async Task BrowserAsync(FullApplicationFixture app, FullApplicationFixture.Account manager,
        FullApplicationFixture.Account viewer, FullApplicationFixture.StoreSeed store, int transactionId, string output)
    {
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "inventory-ledger-period.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", terminalId = store.TerminalId, warehouseId = store.WarehouseId,
            variantId = store.VariantId, transactionId, output,
            manager = new { user = manager.Name, password = manager.Password }, viewer = new { user = viewer.Name, password = viewer.Password }
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log); Assert.True(process.ExitCode == 0, log);
    }
}

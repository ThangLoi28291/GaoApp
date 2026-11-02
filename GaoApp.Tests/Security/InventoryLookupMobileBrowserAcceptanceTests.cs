using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryLookupMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Native_phone_lookup_management_and_reconciliation_preserve_stock_cost_rights_tenant_and_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var administrator = await app.AddAccountAsync(store, "*");
        var staff = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.Balance.View,
            PermissionCodes.Inventory.Transaction.View, PermissionCodes.Inventory.Warehouse.View,
            PermissionCodes.System.LegalEntity.View, PermissionCodes.System.LegalEntity.Reconcile);
        var deniedAccount = await app.AddAccountAsync(store, PermissionCodes.Inventory.Balance.View);
        var foreignAccount = await app.AddAccountAsync(app.Stores[1], "*");
        // Cost visibility is a current-store system ADMIN database role, not a caller flag or '*'.
        await using (var roles = app.Database.CreateHostContext())
        {
            var role = await roles.Roles.SingleAsync(x => x.StoreId == store.StoreId && x.Code == "ADMIN" && x.IsSystemRole);
            (await roles.UserInStores.SingleAsync(x => x.StoreId == store.StoreId && x.UserId == administrator.UserId)).RoleId = role.Id;
            await roles.SaveChangesAsync();
        }
        var receiptSeed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        const string barcode = "2600130000040", cartonBarcode = "2600130000248", sku = "UI13-MILK-LOOKUP";
        int warehouseId, legalEntityId, orderId, transactionId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            product.Sku = sku; product.HasInputInvoice = false;
            var pack = await db.ProductUnitConversions.Include(x => x.Unit).SingleAsync(x => x.Id == receiptSeed.PackId);
            var carton = await db.ProductUnitConversions.Include(x => x.Unit).SingleAsync(x => x.Id == receiptSeed.CartonId);
            pack.IsActive = true; pack.Unit.IsActive = true; carton.IsActive = true; carton.Unit.IsActive = true;
            (await db.ProductVariantUnitBarcodes.SingleAsync(x => x.ProductUnitConversionId == pack.Id)).Barcode = barcode;
            (await db.ProductVariantUnitBarcodes.SingleAsync(x => x.ProductUnitConversionId == carton.Id)).Barcode = cartonBarcode;
            var legal = new LegalEntity { StoreId = store.StoreId, Code = "UI13-LEGAL", Name = "HKD kiểm thử UI-13",
                LegalName = "Hộ kinh doanh kiểm thử giao diện UI-13", SalePriority = 2, IsActive = true,
                Note = "UI13 legal original note" };
            db.Add(legal); await db.SaveChangesAsync();
            var warehouse = new Warehouse { StoreId = store.StoreId, LegalEntityId = legal.Id, Code = "UI13-WH",
                Name = "Kho kiểm thử UI-13", Location = "Kệ kho thử UI-13", Note = "UI13 warehouse original note", IsActive = true };
            db.Add(warehouse); await db.SaveChangesAsync(); legal.DefaultWarehouseId = warehouse.Id;
            var sourceLegalId = await db.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptSeed.ReceiptId);
            receipt.DocumentNo = "NK-UI13-SOURCE"; receipt.Status = StockDocumentStatus.Confirmed;
            receipt.ConfirmedLegalEntityId = sourceLegalId;
            var shift = new POSShift { StoreId = store.StoreId, WarehouseId = store.WarehouseId, TerminalId = store.TerminalId,
                OpenedByUserId = administrator.UserId, ClosedByUserId = administrator.UserId, ShiftCode = "UI13-CLOSED-SHIFT",
                Status = POSShiftStatus.Closed, OpenedAtUtc = DateTime.UtcNow.AddDays(-2), ClosedAtUtc = DateTime.UtcNow.AddDays(-1) };
            db.Add(shift); await db.SaveChangesAsync();
            var order = new Order { StoreId = store.StoreId, POSShiftId = shift.Id, OrderNumber = "UI13-RECON-ORDER",
                Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid, Subtotal = 120, GrandTotal = 120,
                PaidTotal = 120, CompletedAtUtc = DateTime.UtcNow, LegalEntityAllocatedAtUtc = DateTime.UtcNow,
                LegalEntityCount = 1, CreatedBy = administrator.UserId };
            var line = new OrderLine { StoreId = store.StoreId, Order = order, ProductId = product.ProductId,
                VariantId = product.Id, ProductUnitConversionId = pack.Id, SellingUnitId = pack.UnitId,
                BaseUnitId = product.Product.BaseUnitId, ItemName = product.Product.Name, Sku = sku, Barcode = barcode,
                SellingUnitName = pack.Unit.Name, Quantity = 3, BaseQuantity = 12, Multiplier = 4,
                UnitPrice = 40, LineTotal = 120 };
            db.Add(line); await db.SaveChangesAsync();
            db.Add(new OrderLegalEntityAllocation { StoreId = store.StoreId, OrderId = order.Id, OrderLineId = line.Id,
                ProductVariantId = product.Id, ProductUnitConversionId = pack.Id, LegalEntityId = legal.Id,
                WarehouseId = warehouse.Id, SalePriority = 2, Quantity = 3, BaseQuantity = 12, UnitPrice = 40,
                LineTotal = 120, NetAmount = 120 });
            // Existing reservation snapshot/read contract: 105 physical -12 reserved =93 available.
            db.Add(new InventoryReservation { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                ProductVariantId = product.Id, ReferenceType = InventoryReferenceType.Order, ReferenceId = order.Id.ToString(),
                ReferenceLineId = line.Id, ReservedQty = 12, Status = InventoryReservationStatus.Active, Note = "UI13 reserved source" });
            await db.SaveChangesAsync();
            var movement = InventoryPosPostingContractTests.CreateRealMovementService(db);
            var sourceLine = receipt.Lines.Single(x => x.ProductUnitConversionId == pack.Id);
            await movement.CreateAsync(new InventoryMovementFactory().CreatePurchaseReceipt(store.WarehouseId, product.Id,
                8, 20, receipt.Id.ToString(), sourceLine.Id, receipt.DocumentNo, sourceLine.LineNo, DateTime.UtcNow.AddMinutes(-2)));
            transactionId = await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == receipt.Id.ToString())
                .Select(x => x.Id).SingleAsync();
            await movement.CreateAsync(new InventoryMovementFactory().CreateAdjustmentDecrease(store.WarehouseId, product.Id,
                3, 10, "UI13 ledger decrease", DateTime.UtcNow.AddMinutes(-1)));
            (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == product.Id)).ReservedQty = 12;
            await db.SaveChangesAsync();
            warehouseId = warehouse.Id; legalEntityId = legal.Id; orderId = order.Id;
        }
        using var admin = await app.LoginAsync(administrator);
        using var readOnly = await app.LoginAsync(viewer);
        using var ordinary = await app.LoginAsync(staff);
        using var denied = await app.LoginAsync(deniedAccount);
        using var foreign = await app.LoginAsync(foreignAccount);
        readOnly.Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        denied.Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var protectedBefore = await ProtectedStateAsync(app, warehouseId, legalEntityId);
        foreach (var keyword in new[] { barcode, cartonBarcode, sku, "sua tuoi khong duong" })
        {
            var result = await admin.JsonAsync(HttpMethod.Get, $"/admin/inventory-inquiry/data?warehouseId={store.WarehouseId}&keyword={Uri.EscapeDataString(keyword)}");
            Assert.True(result.GetProperty("canViewCost").GetBoolean());
            var item = Assert.Single(result.GetProperty("items").EnumerateArray());
            Assert.Equal(105m, item.GetProperty("onHandQty").GetDecimal()); Assert.Equal(12m, item.GetProperty("reservedQty").GetDecimal());
            Assert.Equal(93m, item.GetProperty("availableQty").GetDecimal());
        }
        var quickPath = $"/admin/inventory-inquiry/quick-view?warehouseId={store.WarehouseId}&productVariantId={store.VariantId}";
        var quick = await admin.JsonAsync(HttpMethod.Get, quickPath);
        Assert.Contains(quick.GetProperty("units").EnumerateArray(), x => x.GetProperty("factor").GetDecimal() == 4m && x.GetProperty("barcode").GetString() == barcode);
        Assert.Contains(quick.GetProperty("units").EnumerateArray(), x => x.GetProperty("factor").GetDecimal() == 24m && x.GetProperty("barcode").GetString() == cartonBarcode);
        foreach (var client in new[] { readOnly, ordinary })
        {
            var page = await client.JsonAsync(HttpMethod.Get, $"/admin/inventory-inquiry/data?includeCost=true&canViewCost=true&costViewerUserId={administrator.UserId}");
            Assert.False(page.GetProperty("canViewCost").GetBoolean());
            Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("cost", out _)));
            Assert.False((await client.JsonAsync(HttpMethod.Get, quickPath)).TryGetProperty("cost", out _));
            var ledger = await client.JsonAsync(HttpMethod.Get, "/admin/inventory-ledger/data?includeCost=true");
            Assert.False(ledger.GetProperty("canViewCost").GetBoolean());
            Assert.All(ledger.GetProperty("items").EnumerateArray(), item => {
                Assert.False(item.TryGetProperty("inboundCost", out _)); Assert.False(item.TryGetProperty("unitCostSnapshot", out _));
                Assert.False(item.TryGetProperty("totalCost", out _));
            });
        }
        var source = (await admin.JsonAsync(HttpMethod.Get, $"/admin/inventory-ledger/quick-view?transactionId={transactionId}")).GetProperty("item");
        Assert.Equal(100m, source.GetProperty("beforeQty").GetDecimal()); Assert.Equal(8m, source.GetProperty("quantityChange").GetDecimal());
        Assert.Equal(108m, source.GetProperty("afterQty").GetDecimal()); Assert.Equal("NK-UI13-SOURCE", source.GetProperty("receiptDocumentNo").GetString());
        Assert.True(source.TryGetProperty("inboundCost", out _));
        var cross = await admin.JsonAsync(HttpMethod.Get, $"/admin/inventory-inquiry/data?warehouseId={app.Stores[1].WarehouseId}");
        Assert.Empty(cross.GetProperty("items").EnumerateArray());
        foreach (var path in new[] { quickPath, $"/admin/inventory-ledger/quick-view?transactionId={transactionId}" })
        { using var response = await foreign.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        foreach (var path in new[] { "/admin/inventory-ledger", "/Admin/WarehouseManagement/Index", "/Admin/LegalEntityManagement/Index", "/admin/legal-entities/reconciliation" })
        { using var response = await denied.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        foreach (var (method,path,body) in new[] {
            (HttpMethod.Put,"/admin/api/warehouses",(object)new { id = warehouseId, code = "UI13-WH", name = "denied" }),
            (HttpMethod.Put,$"/admin/api/legal-entities/{legalEntityId}",(object)new { id = legalEntityId, code = "UI13-LEGAL", name = "denied", legalName = "denied", salePriority = 2 }),
            (HttpMethod.Patch,"/admin/api/legal-entities/feature-state",(object)new { isEnabled = true, expectedCurrentState = false, reason = "denied" }) })
        { using var response = await readOnly.Http.SendAsync(new HttpRequestMessage(method,path) { Content = JsonContent.Create(body) }); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        var token = admin.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await admin.Http.PutAsJsonAsync("/admin/api/warehouses",new { id = warehouseId, code = "UI13-WH", name = "csrf-denied" })) Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using (var response = await admin.Http.PutAsJsonAsync($"/admin/api/legal-entities/{legalEntityId}",new { id = legalEntityId, code = "UI13-LEGAL", name = "csrf-denied", legalName = "csrf-denied", salePriority = 2 })) Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        admin.Http.DefaultRequestHeaders.Add("RequestVerificationToken",token);
        Assert.Equal(protectedBefore,await ProtectedStateAsync(app,warehouseId,legalEntityId));
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(Environment.GetEnvironmentVariable("GAO_UI13_REVIEW_OUTPUT") ?? Path.Combine(root,"TestResults","mobile-ui-13","browser"),"lookup");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","inventory-lookup-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", terminalId = store.TerminalId, output,
            manager = new { user = administrator.Name, password = administrator.Password }, viewer = new { user = viewer.Name, password = viewer.Password },
            warehouseId, sourceWarehouseId = store.WarehouseId, legalEntityId, orderId, transactionId,
            receiptId = receiptSeed.ReceiptId, variantId = store.VariantId, barcode, cartonBarcode, sku,
            fromDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), toDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);
        Assert.True(process.ExitCode == 0,log);
        Assert.Equal(protectedBefore,await ProtectedStateAsync(app,warehouseId,legalEntityId));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal("UI13 native warehouse note",await verify.Warehouses.Where(x => x.Id == warehouseId).Select(x => x.Note).SingleAsync());
        Assert.Equal("UI13 native legal note",await verify.LegalEntities.Where(x => x.Id == legalEntityId).Select(x => x.Note).SingleAsync());
        var final = await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        Assert.Equal(105m,final.OnHandQty); Assert.Equal(12m,final.ReservedQty); Assert.Equal(93m,final.AvailableQty);
        Assert.Equal(3,await verify.InventoryTransactions.CountAsync());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new {
            passed = true, currentStoreSystemAdminCostOnly = true, wildcardPermissionAloneDoesNotGrantCost = true,
            decimalUnitFactorsAndNumericBarcodePreserved = true, onHand = 105m, reserved = 12m, available = 93m,
            ledgerReceiptBefore = 100m, ledgerReceiptChange = 8m, ledgerReceiptAfter = 108m,
            ledgerFinalAfterDecrease = 105m, ledgerSourceDocument = "NK-UI13-SOURCE", nativeReadOnlyTenantScope = true,
            nativeManagementNotesOnly = true, storesMenusFeaturesSettingsRolesCatalogOtherStoreAndInventoryUnchanged = true,
            legalEntityId, warehouseId, orderId, transactionId, receiptId = receiptSeed.ReceiptId
        },new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ProtectedStateAsync(FullApplicationFixture app,int editableWarehouseId,int editableLegalId)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>(params int[] noteOnlyIds) where TEntity : BaseEntity
        {
            var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var mutableAudit = new[] { "Note","RowVersion","UpdatedAtUtc","UpdatedBy" };
            return rows.Select(row => db.Entry(row).Properties.OrderBy(x => x.Metadata.Name)
                .Where(property => !noteOnlyIds.Contains(row.Id) || !mutableAudit.Contains(property.Metadata.Name))
                .ToDictionary(property => property.Metadata.Name,property => property.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new {
            stores = await Rows<Store>(), menus = await Rows<AdminMenuItem>(), roles = await Rows<Role>(),
            grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.RoleId,x.PermissionId }).ToListAsync(),
            warehouses = await Rows<Warehouse>(editableWarehouseId), legalEntities = await Rows<LegalEntity>(editableLegalId),
            settings = await Rows<InvoiceProviderSetting>(), autoInvoice = await Rows<AutoInvoiceSettings>(), rewards = await Rows<RewardSettings>(),
            products = await Rows<Product>(), variants = await Rows<ProductVariant>(), units = await Rows<Unit>(),
            conversions = await Rows<ProductUnitConversion>(), barcodes = await Rows<ProductVariantUnitBarcode>(),
            stock = await Rows<InventoryBalance>(), ledger = await Rows<InventoryTransaction>(), layers = await Rows<InventoryCostLayer>(),
            allocations = await Rows<InventoryCostLayerAllocation>(), valuations = await Rows<InventoryValuationEntry>(), reservations = await Rows<InventoryReservation>(),
            orders = await Rows<Order>(), orderLines = await Rows<OrderLine>(), legalAllocations = await Rows<OrderLegalEntityAllocation>(),
            documents = await Rows<StockDocument>(), documentLines = await Rows<StockDocumentLine>()
        });
    }
}

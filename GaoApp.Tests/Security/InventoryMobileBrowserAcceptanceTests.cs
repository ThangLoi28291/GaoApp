using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Native_phone_inventory_flows_preserve_units_lifecycle_permissions_tenant_and_store_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, "*");
        var reader = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockTransfer.View,
            PermissionCodes.Inventory.Adjustment.View, PermissionCodes.Inventory.Issue.View);
        using var admin = await app.LoginAsync(manager);
        using var readOnly = await app.LoginAsync(reader);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        readOnly.Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        int destinationId, unitId, conversionId, issueId;
        await using (var setup = app.Database.CreateTenantContext(store.StoreId))
        {
            var source = await setup.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
            var variant = await setup.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.ProductVariantName = "Hàng đối chiếu đơn vị UI-12";
            var destination = new Warehouse { StoreId = store.StoreId, LegalEntityId = source.LegalEntityId,
                Code = "UI12-DEST", Name = "Kho nhận UI-12", IsActive = true };
            var unit = new Unit { StoreId = store.StoreId, Code = "UI12-PACK", Name = "Lốc 4", IsActive = true };
            setup.AddRange(destination, unit); await setup.SaveChangesAsync();
            var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                UnitId = unit.Id, Factor = 4, IsActive = true, Price = 80 };
            setup.Add(conversion); await setup.SaveChangesAsync();
            setup.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = conversion.Id,
                Barcode = "2600120000041", IsActive = true, IsPrimary = true });
            var sourceShift = new POSShift { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                TerminalId = store.TerminalId, OpenedByUserId = manager.UserId, ClosedByUserId = manager.UserId,
                ShiftCode = "UI12-SOURCE-SHIFT", Status = POSShiftStatus.Closed,
                OpenedAtUtc = DateTime.UtcNow.AddDays(-3), ClosedAtUtc = DateTime.UtcNow.AddDays(-2) };
            setup.Add(sourceShift); await setup.SaveChangesAsync();
            var order = new Order { StoreId = store.StoreId, OrderNumber = "UI12-ISSUE-SOURCE", Status = OrderStatus.Completed,
                POSShiftId = sourceShift.Id, CompletedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedBy = manager.UserId,
                HasInventoryIssue = true, InventoryResolutionStatus = InventoryResolutionStatus.Approved };
            var orderLine = new OrderLine { StoreId = store.StoreId, Order = order, ProductId = variant.ProductId,
                VariantId = variant.Id, ItemName = "Hàng đối chiếu đơn vị UI-12", Quantity = 3, BaseQuantity = 12,
                SellingUnitId = unit.Id, ProductUnitConversionId = conversion.Id, SellingUnitName = unit.Name,
                Multiplier = 4, BaseUnitId = variant.Product.BaseUnitId };
            setup.Add(orderLine); await setup.SaveChangesAsync();
            var issue = new OrderInventoryIssue { StoreId = store.StoreId, OrderId = order.Id, Code = "UI12-CLOSED-ISSUE",
                Status = InventoryResolutionStatus.Approved, OpenedAtUtc = DateTime.UtcNow.AddDays(-2),
                DueAtUtc = DateTime.UtcNow.AddDays(-1), ApprovedAtUtc = DateTime.UtcNow,
                InternalNote = "Nguồn đối chiếu UI-12; không ghi sổ khi đổi kích thước màn hình." };
            issue.Lines.Add(new OrderInventoryIssueLine { StoreId = store.StoreId, OrderId = order.Id, OrderLineId = orderLine.Id,
                ProductId = variant.ProductId, ProductVariantId = variant.Id, ProductUnitConversionId = conversion.Id,
                OrderedQty = 12, StockBefore = 8, StockAfter = -4, NegativeQty = 4,
                AutoDetectedInboundQty = 4, AutoDetectedDocumentResolved = true, AutoDetectedCostResolved = true,
                IsResolved = true, ProvisionalUnitCost = 10, ProvisionalCostAmount = 40,
                AutoResolveNote = "Đã đủ lượng | Đã có nguồn | Đã đối chiếu giá vốn" });
            setup.Add(issue); await setup.SaveChangesAsync();
            destinationId = destination.Id; unitId = unit.Id; conversionId = conversion.Id; issueId = issue.Id;
        }
        var transfer = await admin.JsonAsync(HttpMethod.Post, "/admin/api/stock-transfers", new {
            documentDate = DateTime.Today, fromWarehouseId = store.WarehouseId, toWarehouseId = destinationId, note = "UI12 transfer source" });
        var transferId = transfer.GetProperty("id").GetInt32();
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-transfers/{transferId}/lines", new {
            productVariantId = store.VariantId, unitId, quantity = 1.25m, note = "Lốc 4, giữ hệ số gốc" });
        var revaluation = await admin.JsonAsync(HttpMethod.Post, "/admin/api/inventory-adjustment-documents", new {
            warehouseId = store.WarehouseId, adjustmentType = 70, reasonType = 7, note = "UI12 revaluation draft retained",
            lines = new[] { new { productVariantId = store.VariantId, unitId, productUnitConversionId = conversionId,
                quantity = 1.5m, unitCost = 10m, note = "Đổi giá vốn không tự ghi sổ" } } });
        var revaluationId = revaluation.GetProperty("id").GetInt32();
        var protectedBefore = await PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
        async Task<string> NativeState()
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            return JsonSerializer.Serialize(new {
                transfers = await db.StockTransferDocuments.AsNoTracking().Include(x => x.Lines).OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Status, x.RowVersion, x.FromWarehouseId, x.ToWarehouseId,
                        lines = x.Lines.Select(l => new { l.Id, l.UnitId, l.Factor, l.Quantity, l.BaseQuantity, l.RowVersion }) }).ToListAsync(),
                adjustments = await db.InventoryAdjustmentDocuments.AsNoTracking().OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync(),
                issues = await db.OrderInventoryIssues.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.InternalNote, x.RowVersion }).ToListAsync(),
                issueActions = await db.OrderInventoryIssueActions.CountAsync(),
                transactionCount = await db.InventoryTransactions.CountAsync(),
                balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync()
            });
        }
        var stateBefore = await NativeState();
        foreach (var endpoint in new[] { $"/admin/api/stock-transfers/{transferId}/submit", $"/admin/api/stock-transfers/{transferId}/confirm",
                     $"/admin/api/inventory-adjustment-documents/{revaluationId}/submit", $"/admin/api/inventory-adjustment-documents/{revaluationId}/approve" })
        {
            using var denied = await readOnly.Http.PostAsJsonAsync(endpoint, new { });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        foreach (var endpoint in new[] { $"/admin/api/stock-transfers/{transferId}", $"/admin/api/inventory-adjustment-documents/{revaluationId}", $"/admin/inventory-issues/{issueId}" })
        {
            using var denied = await foreign.Http.GetAsync(endpoint);
            Assert.False(denied.IsSuccessStatusCode); Assert.NotEqual(HttpStatusCode.InternalServerError, denied.StatusCode);
        }
        using (var denied = await readOnly.Http.GetAsync("/admin/inventory-adjustment-documents/create")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await readOnly.Http.GetAsync($"/admin/inventory-adjustment-documents/{revaluationId}/edit")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await readOnly.Http.PostAsync("/admin/inventory-issues/add-note", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["IssueId"] = issueId.ToString(), ["Note"] = "denied" }))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var csrf = admin.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await admin.Http.PostAsJsonAsync($"/admin/api/stock-transfers/{transferId}/submit", new { })) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var denied = await admin.Http.PostAsJsonAsync($"/admin/api/inventory-adjustment-documents/{revaluationId}/submit", new { })) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var denied = await admin.Http.PostAsync("/admin/inventory-issues/add-note", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["IssueId"] = issueId.ToString(), ["Note"] = "missing csrf" }))) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        admin.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        Assert.Equal(stateBefore, await NativeState());
        using (var legacy = await admin.Http.GetAsync("/admin/inventory-adjustment"))
        {
            Assert.Equal(HttpStatusCode.Redirect, legacy.StatusCode);
            Assert.EndsWith("/admin/inventory-adjustment-documents", legacy.Headers.Location?.ToString());
        }
        int openingTransactionCount;
        await using (var opening = app.Database.CreateTenantContext(store.StoreId)) openingTransactionCount = await opening.InventoryTransactions.CountAsync();
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(Environment.GetEnvironmentVariable("GAO_UI12_REVIEW_OUTPUT") ?? Path.Combine(root,"TestResults","mobile-ui-12","browser"),"operations");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","inventory-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", terminalId = store.TerminalId, output,
            manager = new { user = manager.Name, password = manager.Password }, viewer = new { user = reader.Name, password = reader.Password },
            transferId, revaluationId, issueId, warehouseId = store.WarehouseId, unitId, conversionId,
            barcode = "2600120000041", variantId = store.VariantId
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
        var browserResult = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(output,"browser-checks.json")));
        var adjustmentId = browserResult.GetProperty("adjustmentId").GetInt32();
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        var savedTransfer = await verify.StockTransferDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == transferId);
        Assert.Equal(StockTransferDocumentStatus.Confirmed, savedTransfer.Status);
        var transferLine = Assert.Single(savedTransfer.Lines);
        Assert.Equal(unitId, transferLine.UnitId); Assert.Equal(4m, transferLine.Factor);
        Assert.Equal(2.5m, transferLine.Quantity); Assert.Equal(10m, transferLine.BaseQuantity);
        var movementPair = await verify.InventoryTransactions.AsNoTracking().Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer && x.ReferenceId == transferId.ToString()).ToListAsync();
        Assert.Equal(2, movementPair.Count);
        var outgoing = Assert.Single(movementPair, x => x.TransactionType == InventoryTransactionType.TransferOut);
        var incoming = Assert.Single(movementPair, x => x.TransactionType == InventoryTransactionType.TransferIn);
        Assert.Equal(store.WarehouseId, outgoing.WarehouseId); Assert.Equal(destinationId, incoming.WarehouseId);
        Assert.Equal(-10m, outgoing.QuantityChange); Assert.Equal(10m, incoming.QuantityChange);
        Assert.Equal(transferLine.Id, outgoing.ReferenceLineId); Assert.Equal(transferLine.Id, incoming.ReferenceLineId);
        Assert.Equal(-outgoing.TotalCost, incoming.TotalCost);
        var adjustment = await verify.InventoryAdjustmentDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == adjustmentId);
        Assert.Equal(InventoryAdjustmentDocumentStatus.Approved, adjustment.Status);
        Assert.Equal(InventoryTransactionType.AdjustmentIncrease, adjustment.AdjustmentType);
        var adjustmentLine = Assert.Single(adjustment.Lines);
        Assert.Equal(unitId, adjustmentLine.UnitId); Assert.Equal(4m, adjustmentLine.Factor);
        Assert.Equal(1.5m, adjustmentLine.Quantity); Assert.Equal(6m, adjustmentLine.BaseQuantity); Assert.Equal(10m, adjustmentLine.UnitCost);
        var adjustmentLedger = await verify.InventoryTransactions.AsNoTracking().Where(x => x.ReferenceType == InventoryReferenceType.Adjustment && x.ReferenceId == adjustmentId.ToString()).ToListAsync();
        var adjustmentMovement = Assert.Single(adjustmentLedger);
        Assert.Equal(6m, adjustmentMovement.QuantityChange); Assert.Equal(60m, adjustmentMovement.TotalCost);
        Assert.Equal(adjustmentLine.Id, adjustmentMovement.ReferenceLineId);
        Assert.Equal(store.WarehouseId, adjustmentMovement.WarehouseId);
        Assert.Equal(openingTransactionCount + 3, await verify.InventoryTransactions.CountAsync());
        var untouchedRevaluation = await verify.InventoryAdjustmentDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == revaluationId);
        Assert.Equal(InventoryAdjustmentDocumentStatus.Draft, untouchedRevaluation.Status);
        Assert.Equal(InventoryTransactionType.Revaluation, untouchedRevaluation.AdjustmentType);
        Assert.Equal(1.5m, Assert.Single(untouchedRevaluation.Lines).Quantity); Assert.Equal(6m, Assert.Single(untouchedRevaluation.Lines).BaseQuantity);
        Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Adjustment && x.ReferenceId == revaluationId.ToString()).ToListAsync());
        Assert.Equal(96m, await verify.InventoryBalances.Where(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId).Select(x => x.OnHandQty).SingleAsync());
        Assert.Equal(10m, await verify.InventoryBalances.Where(x => x.WarehouseId == destinationId && x.ProductVariantId == store.VariantId).Select(x => x.OnHandQty).SingleAsync());
        var issueAfter = await verify.OrderInventoryIssues.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == issueId);
        Assert.Equal(InventoryResolutionStatus.Approved, issueAfter.Status);
        Assert.Equal(4m, Assert.Single(issueAfter.Lines).NegativeQty); Assert.Equal(12m, Assert.Single(issueAfter.Lines).OrderedQty);
        Assert.Equal("Nguồn đối chiếu UI-12; không ghi sổ khi đổi kích thước màn hình.", issueAfter.InternalNote);
        var note = Assert.Single(await verify.OrderInventoryIssueActions.AsNoTracking().Where(x => x.OrderInventoryIssueId == issueId).ToListAsync());
        Assert.Equal(InventoryIssueActionType.NoteAdded, note.ActionType); Assert.Equal("UI12 native phone note", note.Note);
        Assert.Equal(protectedBefore, await PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store));
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, transferId, adjustmentId, revaluationId, issueId, nativeDecimalFactor4Preserved = true,
            transferQuantity = transferLine.Quantity, transferBaseQuantity = transferLine.BaseQuantity,
            adjustmentQuantity = adjustmentLine.Quantity, adjustmentBaseQuantity = adjustmentLine.BaseQuantity,
            pairedTransferMovements = movementPair.Count, adjustmentMovements = adjustmentLedger.Count,
            originalStock = 100m, finalSourceStock = 96m, finalDestinationStock = 10m,
            revaluationDraftAndIssueSourceQuantitiesPreserved = true, permissionsTenantCsrfAndLegacyRedirectVerified = true,
            storeMenusConfigurationCatalogAndOtherStoreUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

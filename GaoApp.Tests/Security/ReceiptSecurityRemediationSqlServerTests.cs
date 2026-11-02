using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Purchases;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

/// <summary>HTTP-to-SQL regressions for the receipt security findings; no fake authorization/services.</summary>
[Collection("SqlServerConcurrency")]
public sealed class ReceiptSecurityRemediationSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}";

    [Fact]
    public async Task Wrong_parent_cannot_borrow_purchase_approval_to_edit_or_delete_a_direct_receipt_line()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var purchaseId = await CloneReceipt(app, store, seed.ReceiptId, purchase: true);
        using var purchaseManager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Delete,
            PermissionCodes.Purchase.Receipt.Approve));
        string before;
        StockDocument document;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            document = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            before = await MutationEvidence(db);
        }
        var line = document.Lines.OrderBy(x => x.Id).First();
        using (var rejected = await purchaseManager.Http.PutAsJsonAsync(Url(purchaseId) + $"/lines/{line.Id}", new UpdateStockDocumentLineRequest
        {
            RowVersion = Convert.ToBase64String(document.RowVersion), LineRowVersion = Convert.ToBase64String(line.RowVersion),
            UnitId = line.UnitId, Quantity = 999, UnitCost = 77777, Note = "Không được ghi dù trả lỗi"
        })) Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        using (var rejected = await Delete(purchaseManager, purchaseId, line.Id, new()
        {
            RowVersion = Convert.ToBase64String(document.RowVersion), LineRowVersion = Convert.ToBase64String(line.RowVersion)
        })) Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await MutationEvidence(final));
    }

    [Fact]
    public async Task Physical_edits_require_current_receipt_and_line_versions_and_valid_versions_still_update_and_delete()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var original = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        var line = original.GetProperty("lines")[0];
        var lineId = line.GetProperty("id").GetInt32();
        var documentVersion = original.GetProperty("rowVersion").GetString()!;
        var lineVersion = line.GetProperty("rowVersion").GetString()!;
        var fakeVersion = Convert.ToBase64String(new byte[8]);
        UpdateStockDocumentLineRequest Edit(string? documentRv, string? lineRv) => new()
        {
            RowVersion = documentRv, LineRowVersion = lineRv, UnitId = line.GetProperty("unitId").GetInt32(),
            Quantity = 3, UnitCost = 77, Note = "Cập nhật có đối chiếu phiên bản"
        };
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await MutationEvidence(db);
        foreach (var invalid in new[] { Edit(null, null), Edit(documentVersion, null), Edit(null, lineVersion),
            Edit(fakeVersion, lineVersion), Edit(documentVersion, fakeVersion), Edit("invalid-base64", lineVersion) })
        {
            using var rejected = await manager.Http.PutAsJsonAsync(Url(seed.ReceiptId) + $"/lines/{lineId}", invalid);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        using (var rejected = await Delete(manager, seed.ReceiptId, lineId, new()))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) Assert.Equal(before, await MutationEvidence(db));

        var saved = await manager.JsonAsync(HttpMethod.Put, Url(seed.ReceiptId) + $"/lines/{lineId}", Edit(documentVersion, lineVersion));
        var currentDocumentVersion = saved.GetProperty("rowVersion").GetString()!;
        var currentLineVersion = saved.GetProperty("lineRowVersion").GetString()!;
        Assert.NotEqual(documentVersion, currentDocumentVersion);
        Assert.NotEqual(lineVersion, currentLineVersion);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var persisted = await db.StockDocumentLines.SingleAsync(x => x.Id == lineId);
            Assert.Equal(3m, persisted.Quantity); Assert.Equal(77m, persisted.UnitCost);
            Assert.Equal(3m * persisted.Factor, persisted.BaseQuantity);
            before = await MutationEvidence(db);
        }
        foreach (var invalid in new[] { Edit(documentVersion, lineVersion), Edit(currentDocumentVersion, lineVersion) })
        {
            using var rejected = await manager.Http.PutAsJsonAsync(Url(seed.ReceiptId) + $"/lines/{lineId}", invalid);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        foreach (var invalid in new[] { new DeleteStockDocumentLineRequest { RowVersion = documentVersion, LineRowVersion = lineVersion },
            new DeleteStockDocumentLineRequest { RowVersion = currentDocumentVersion, LineRowVersion = lineVersion } })
        {
            using var rejected = await Delete(manager, seed.ReceiptId, lineId, invalid);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) Assert.Equal(before, await MutationEvidence(db));
        using (var deleted = await Delete(manager, seed.ReceiptId, lineId, new()
        {
            RowVersion = currentDocumentVersion, LineRowVersion = currentLineVersion
        })) Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await final.StockDocumentLines.AnyAsync(x => x.Id == lineId && !x.IsDeleted));
        Assert.Equal(1, await final.StockDocumentLines.CountAsync(x => x.StockDocumentId == seed.ReceiptId));
        Assert.False(await final.PurchasePayables.AnyAsync());
        Assert.False(await final.TreasuryEntries.AnyAsync());
    }

    [Fact]
    public async Task Every_pricing_endpoint_uses_target_source_permissions_and_denials_leave_no_plan_or_price_changes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        var purchaseId = await CloneReceipt(app, store, seed.ReceiptId, purchase: true, pending: true);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var direct = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var purchase = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Approve));
        int inventoryBefore;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) inventoryBefore = await db.InventoryTransactions.CountAsync();

        foreach (var pair in new[] { (Id: seed.ReceiptId, Allowed: direct, Denied: purchase), (Id: purchaseId, Allowed: purchase, Denied: direct) })
        {
            var url = PricingAllocationTestData.Url(pair.Id);
            var workspace = await PricingAllocationTestData.Get(admin, url);
            var request = PricingAllocationTestData.Request(workspace);
            string before;
            await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await MutationEvidence(db);
            using (var denied = await pair.Denied.Http.GetAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await pair.Denied.Http.PostAsJsonAsync(url + "/preview", request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await pair.Denied.Http.PutAsJsonAsync(url, request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await pair.Denied.Http.PostAsJsonAsync(url + "/apply", new PurchaseReceiptPricingApplyRequest
            {
                ReceiptRowVersion = workspace.ReceiptRowVersion, PlanRowVersion = workspace.PlanRowVersion ?? ""
            })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await using (var db = app.Database.CreateTenantContext(store.StoreId)) Assert.Equal(before, await MutationEvidence(db));

            await PricingAllocationTestData.Get(pair.Allowed, url);
            await pair.Allowed.JsonAsync(HttpMethod.Post, url + "/preview", request);
            var saved = PricingAllocationTestData.Read(await pair.Allowed.JsonAsync(HttpMethod.Put, url, request));
            var applied = PricingAllocationTestData.Read(await pair.Allowed.JsonAsync(HttpMethod.Post, url + "/apply", new PurchaseReceiptPricingApplyRequest
            {
                ReceiptRowVersion = saved.ReceiptRowVersion, PlanRowVersion = saved.PlanRowVersion ?? ""
            }));
            Assert.True(applied.Preview!.CanApply, string.Join("; ", applied.Preview.Errors));
            await using var check = app.Database.CreateTenantContext(store.StoreId);
            Assert.All(await check.StockDocumentLines.Where(x => x.StockDocumentId == pair.Id).ToListAsync(), x => Assert.Equal(100m, x.UnitPriceBeforeVat));
            Assert.False(await check.PurchasePayables.AnyAsync());
            Assert.Equal(inventoryBefore, await check.InventoryTransactions.CountAsync());
        }
    }

    [Fact]
    public async Task Receipt_reader_and_creator_responses_redact_commercial_values_but_authorized_manager_keeps_them()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await AddCommercialEvidence(app, store, seed.ReceiptId);
        using var reader = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var creator = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Create,
            PermissionCodes.Inventory.StockDocument.View));
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.Inventory.StockDocument.Approve));
        using var costViewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.Purchase.Order.ViewCost));
        foreach (var client in new[] { reader, creator })
        {
            var receipt = await client.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
            foreach (var field in new[] { "totalAmount", "subtotalBeforeVat", "vatAmount", "freightTotal", "freightAllocatedTotal", "freightAllocationDifference" })
                Assert.Equal(0m, receipt.GetProperty(field).GetDecimal());
            foreach (var line in receipt.GetProperty("lines").EnumerateArray())
            {
                foreach (var field in new[] { "unitCost", "lineTotal", "unitPriceBeforeVat", "taxRate", "vatAmount", "unitPriceAfterVat", "freightAllocation", "amountDifference" })
                    Assert.Equal(0m, line.GetProperty(field).GetDecimal());
                foreach (var field in new[] { "editableUnitPriceBeforeVat", "lastPurchaseUnitPriceBeforeVat", "xmlLineAmount", "taxId", "suggestedTaxId", "taxNameSnapshot" })
                    Assert.Equal(JsonValueKind.Null, line.GetProperty(field).ValueKind);
                Assert.True(line.GetProperty("quantity").GetDecimal() > 0);
            }
            var list = await client.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
            var item = Assert.Single(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == seed.ReceiptId);
            Assert.False(item.GetProperty("canViewCost").GetBoolean());
            Assert.Equal(0m, item.GetProperty("totalAmount").GetDecimal());
        }
        var lookupUrl = "/admin/api/stock-documents/search-products?keyword=" + Uri.EscapeDataString("Sữa tươi") + "&stockDocumentId=" + seed.ReceiptId;
        var creatorLookup = await creator.JsonAsync(HttpMethod.Get, lookupUrl);
        Assert.NotEmpty(creatorLookup.EnumerateArray());
        foreach (var item in creatorLookup.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("costPrice").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("price").ValueKind);
        }
        foreach (var client in new[] { manager, costViewer })
        {
            var detail = await client.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
            Assert.True(detail.GetProperty("totalAmount").GetDecimal() > 0);
            Assert.True(detail.GetProperty("lines")[0].GetProperty("editableUnitPriceBeforeVat").GetDecimal() > 0);
            Assert.Equal(24680m, Assert.Single(detail.GetProperty("lines").EnumerateArray(),
                x => x.GetProperty("xmlLineAmount").ValueKind == JsonValueKind.Number).GetProperty("xmlLineAmount").GetDecimal());
            var list = await client.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
            var item = Assert.Single(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == seed.ReceiptId);
            Assert.True(item.GetProperty("canViewCost").GetBoolean());
            Assert.True(item.GetProperty("totalAmount").GetDecimal() > 0);
        }
        var managerLookup = await manager.JsonAsync(HttpMethod.Get, lookupUrl);
        Assert.Contains(managerLookup.EnumerateArray(), x => x.GetProperty("costPrice").ValueKind == JsonValueKind.Number && x.GetProperty("costPrice").GetDecimal() > 0);
        Assert.Contains(managerLookup.EnumerateArray(), x => x.GetProperty("price").ValueKind == JsonValueKind.Number && x.GetProperty("price").GetDecimal() > 0);
    }

    [Fact]
    public async Task Unpaid_approval_creates_outstanding_debts_and_only_real_treasury_payments_set_paid_and_lock_supplier()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.Inventory.StockDocument.Approve));
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Report.Profit.View, PermissionCodes.Report.Treasury.Manage));
        ApprovePurchaseReceiptCommercialRequest request;
        int stockBefore;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            document.Status = StockDocumentStatus.PendingApproval; document.IsMerchandisePaid = false;
            await db.SaveChangesAsync(); stockBefore = await db.InventoryTransactions.CountAsync();
            request = new()
            {
                RowVersion = Convert.ToBase64String(document.RowVersion), SupplierId = document.SupplierId,
                IsMerchandisePaid = false, HasFreight = true, FreightTotal = 1000, FreightPayeeName = "Đơn vị vận chuyển riêng",
                IsFreightPaid = false, WaitForInputInvoice = true,
                Lines = document.Lines.Select(x => new PurchaseReceiptFinancialLineInputDto { StockDocumentLineId = x.Id, UnitPriceBeforeVat = 10000 }).ToList()
            };
        }
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + "/approve-commercial", request);
        List<PurchasePayable> payables;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            payables = await db.PurchasePayables.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).OrderBy(x => x.Type).ToListAsync();
            Assert.Equal(2, payables.Count); Assert.All(payables, x => { Assert.Equal(PurchasePayableStatus.Outstanding, x.Status); Assert.Null(x.PaidAtUtc); });
            Assert.False(await db.TreasuryEntries.AnyAsync());
            Assert.Equal(stockBefore + 2, await db.InventoryTransactions.CountAsync());
        }
        var detail = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.False(detail.GetProperty("isMerchandisePaid").GetBoolean());
        Assert.False(detail.GetProperty("isFreightPaid").GetBoolean());
        foreach (var payable in payables)
        {
            var payment = new TreasuryWriteDto
            {
                ClientRequestId = Guid.NewGuid(), Date = DateTime.UtcNow.AddHours(7).Date, Fund = "bank",
                Amount = -payable.Amount, Name = payable.Type == PurchasePayableType.Merchandise ? "Trả tiền hàng có chứng từ" : "Trả cước có chứng từ",
                Reference = "UNC-RECEIPT-" + payable.Id, PurchasePayableId = payable.Id,
                SourceRowVersion = Convert.ToBase64String(payable.RowVersion)
            };
            using (var denied = await manager.Http.PostAsJsonAsync("/admin/reports/treasury/entries", payment))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await cashier.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", payment);
        }
        detail = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.True(detail.GetProperty("isMerchandisePaid").GetBoolean());
        Assert.True(detail.GetProperty("isFreightPaid").GetBoolean());
        var actions = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/document-actions");
        Assert.False(actions.GetProperty("canCorrectSupplier").GetBoolean());
        Assert.Contains("thanh toán", actions.GetProperty("document").GetProperty("supplierCorrectionBlockedReason").GetString());
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.All(await final.PurchasePayables.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync(), x =>
        { Assert.Equal(PurchasePayableStatus.Paid, x.Status); Assert.NotNull(x.PaidAtUtc); });
        Assert.Equal(2, await final.TreasuryEntries.CountAsync());
        Assert.Equal(stockBefore + 2, await final.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Legacy_purchase_order_link_uses_purchase_permissions_even_when_stored_source_says_direct()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var id = await CloneReceipt(app, store, seed.ReceiptId, purchase: true, pending: true);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var legacy = await db.StockDocuments.SingleAsync(x => x.Id == id);
            legacy.ReceiptSource = PurchaseReceiptSource.Direct;
            legacy.TotalAmount = 12345;
            await db.SaveChangesAsync();
        }
        using var direct = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Approve));
        using var purchase = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.Purchase.Receipt.View, PermissionCodes.Purchase.Receipt.Approve));
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await MutationEvidence(db);
        var detail = await direct.JsonAsync(HttpMethod.Get, Url(id));
        Assert.Equal(0m, detail.GetProperty("totalAmount").GetDecimal());
        Assert.All(detail.GetProperty("lines").EnumerateArray(), x =>
            Assert.Equal(JsonValueKind.Null, x.GetProperty("editableUnitPriceBeforeVat").ValueKind));
        var list = await direct.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        var item = Assert.Single(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.False(item.GetProperty("canViewCost").GetBoolean());
        foreach (var path in new[] { $"/admin/stock-documents/{id}", $"/admin/stock-documents/lines-table?id={id}",
            PricingAllocationTestData.Url(id), Url(id) + "/intake" })
        {
            using var denied = await direct.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var permitted = await purchase.JsonAsync(HttpMethod.Get, Url(id));
        Assert.Equal(12345m, permitted.GetProperty("totalAmount").GetDecimal());
        Assert.True(permitted.GetProperty("lines")[0].GetProperty("editableUnitPriceBeforeVat").GetDecimal() > 0);
        using (var page = await purchase.Http.GetAsync($"/admin/stock-documents/{id}")) Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var pricingUrl = PricingAllocationTestData.Url(id);
        var workspace = await PricingAllocationTestData.Get(purchase, pricingUrl);
        Assert.False(workspace.CanEdit);
        var pricingRequest = PricingAllocationTestData.Request(workspace);
        using (var rejected = await purchase.Http.PostAsJsonAsync(pricingUrl + "/preview", pricingRequest))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("Nguồn phiếu không khớp", await rejected.Content.ReadAsStringAsync());
        }
        using (var rejected = await purchase.Http.PutAsJsonAsync(pricingUrl, pricingRequest))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("Nguồn phiếu không khớp", await rejected.Content.ReadAsStringAsync());
        }
        using (var rejected = await purchase.Http.PostAsJsonAsync(pricingUrl + "/apply", new PurchaseReceiptPricingApplyRequest
        {
            ReceiptRowVersion = workspace.ReceiptRowVersion, PlanRowVersion = workspace.PlanRowVersion ?? ""
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("Nguồn phiếu không khớp", await rejected.Content.ReadAsStringAsync());
        }
        using (var rejected = await purchase.Http.PostAsJsonAsync(Url(id) + "/price-draft", new
        {
            rowVersion = workspace.ReceiptRowVersion,
            lines = workspace.PhysicalLines.Select(x => new
            { stockDocumentLineId = x.StockDocumentLineId, rowVersion = x.RowVersion, unitPriceBeforeVat = 98765m }).ToArray()
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("Nguồn phiếu nhập không khớp", await rejected.Content.ReadAsStringAsync());
        }
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await MutationEvidence(final));
        Assert.Equal(PurchaseReceiptSource.Direct, (await final.StockDocuments.SingleAsync(x => x.Id == id)).ReceiptSource);
    }

    [Fact]
    public async Task Audit_timeline_also_requires_target_commercial_access_before_returning_price_evidence()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var purchaseId = await CloneReceipt(app, store, seed.ReceiptId, purchase: true);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            foreach (var document in await db.StockDocuments.Where(x => x.Id == seed.ReceiptId || x.Id == purchaseId).ToListAsync())
            {
                document.Status = StockDocumentStatus.PendingApproval;
                document.IsMerchandisePaid = false;
            }
            await db.SaveChangesAsync();
        }
        // Generate price evidence through the authenticated manager's real HTTP action.
        // AppDbContext must derive the actor from that request rather than accepting a supplied actor ID.
        foreach (var id in new[] { seed.ReceiptId, purchaseId })
        {
            var document = await manager.JsonAsync(HttpMethod.Get, Url(id));
            await manager.JsonAsync(HttpMethod.Post, Url(id) + "/price-draft", new
            {
                rowVersion = document.GetProperty("rowVersion").GetString(),
                lines = document.GetProperty("lines").EnumerateArray().Select(x => new
                {
                    stockDocumentLineId = x.GetProperty("id").GetInt32(), rowVersion = x.GetProperty("rowVersion").GetString(),
                    unitPriceBeforeVat = 56780m
                }).ToArray()
            });
        }
        using var auditorOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View,
            PermissionCodes.System.AuditLog.View));
        using var wrongSource = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.AuditLog.View,
            PermissionCodes.Inventory.StockDocument.Approve));
        using var costAuditor = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.AuditLog.View,
            PermissionCodes.Purchase.Order.ViewCost));
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await MutationEvidence(db);
        foreach (var id in new[] { seed.ReceiptId, purchaseId })
        {
            using (var denied = await auditorOnly.Http.GetAsync(Url(id) + "/audit-events")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            foreach (var client in new[] { manager, costAuditor })
            {
                var audit = await client.JsonAsync(HttpMethod.Get, Url(id) + "/audit-events");
                Assert.Contains(audit.GetProperty("events").EnumerateArray(), x =>
                    x.GetProperty("newValuesJson").GetString()!.Contains("56780", StringComparison.Ordinal));
            }
        }
        using (var denied = await wrongSource.Http.GetAsync(Url(purchaseId) + "/audit-events")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await wrongSource.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId) + "/audit-events");
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await MutationEvidence(final));
    }

    [Fact]
    public async Task Inconsistent_legacy_purchase_source_blocks_every_intake_mutation_before_lease_or_catalog_work()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        // Enable the compatibility API only in this fixture-owned child process.
        var process = (System.Diagnostics.Process)typeof(FullApplicationFixture)
            .GetField("process", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app)!;
        process.StartInfo.Environment["ReceivingWorkbench__ProvisionalItemsEnabled"] = "true";
        await app.RestartAsync(enableDeliveryOutbox: false);
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var id = await CloneReceipt(app, store, seed.ReceiptId, purchase: true);
        string version, itemVersion;
        int itemId, unitId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.SingleAsync(x => x.Id == id);
            document.ReceiptSource = PurchaseReceiptSource.Direct;
            unitId = (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).UnitId;
            var item = new StockDocumentProvisionalItem { StoreId = store.StoreId, StockDocumentId = id,
                NameSnapshot = "Hàng chờ kiểm tra nguồn", UnitId = unitId, UnitNameSnapshot = "Lốc", Quantity = 1 };
            db.Add(item); await db.SaveChangesAsync();
            version = Convert.ToBase64String(document.RowVersion); itemVersion = Convert.ToBase64String(item.RowVersion); itemId = item.Id;
        }
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Purchase.Receipt.View, PermissionCodes.Purchase.Receipt.Update));
        var state = await employee.JsonAsync(HttpMethod.Get, Url(id) + "/intake");
        Assert.DoesNotContain(state.GetProperty("state").GetProperty("permittedActions").EnumerateArray(), x => x.GetString() == "Capture");
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await MutationEvidence(db);
        var attempts = new (string Path, object Request)[]
        {
            (Url(id) + "/intake/known", new RecordKnownReceiptItemRequest { CommandId = Guid.NewGuid(), DocumentRowVersion = version,
                ProductUnitConversionId = seed.PackId, Factor = 4, Quantity = 1 }),
            (Url(id) + "/intake", new CaptureReceiptIntakeRequest { CommandId = Guid.NewGuid(), DocumentRowVersion = version,
                Name = "Hàng mới", UnitId = unitId, UnitName = "Lốc", Factor = 4, Quantity = 1 }),
            (Url(id) + $"/intake/{itemId}/quantity", new UpdateReceiptIntakeQuantityRequest { CommandId = Guid.NewGuid(),
                DocumentRowVersion = version, ItemRowVersion = itemVersion, Quantity = 9 }),
            (Url(id) + $"/intake/{itemId}/remove", new RemoveProvisionalItemRequest { CommandId = Guid.NewGuid(),
                DocumentRowVersion = version, ItemRowVersion = itemVersion }),
            (Url(id) + "/provisional-items", new CaptureProvisionalItemRequest { CommandId = Guid.NewGuid(),
                DocumentRowVersion = version, Name = "Hàng mới qua API cũ", UnitId = unitId, UnitName = "Lốc", Quantity = 1 })
        };
        foreach (var attempt in attempts)
        {
            using var rejected = await employee.Http.PostAsJsonAsync(attempt.Path, attempt.Request);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Contains("Nguồn phiếu không khớp", await rejected.Content.ReadAsStringAsync());
        }
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await MutationEvidence(final));
    }

    private static Task<HttpResponseMessage> Delete(FullApplicationFixture.Client client, int documentId, int lineId, DeleteStockDocumentLineRequest request)
        => client.Http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Url(documentId) + $"/lines/{lineId}") { Content = JsonContent.Create(request) });

    private static async Task<int> CloneReceipt(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int sourceId, bool purchase, bool pending = false)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var source = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == sourceId);
        int? orderId = null;
        if (purchase)
        {
            var order = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "SEC-PO-" + Guid.NewGuid().ToString("N")[..10],
                SupplierId = source.SupplierId!.Value, ExpectedWarehouseId = store.WarehouseId,
                LegalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId };
            db.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
        }
        var document = new StockDocument
        {
            StoreId = store.StoreId, DocumentNo = "SEC-RECEIPT-" + Guid.NewGuid().ToString("N")[..10], Type = StockDocumentType.Receipt,
            WarehouseId = store.WarehouseId, SupplierId = source.SupplierId, PurchaseOrderId = orderId,
            ReceiptSource = purchase ? PurchaseReceiptSource.PurchaseOrder : PurchaseReceiptSource.Direct,
            DirectReceiptReason = purchase ? null : "Kiểm thử bảo mật", Status = pending ? StockDocumentStatus.PendingApproval : StockDocumentStatus.Draft,
            IsMerchandisePaid = false,
            Lines = source.Lines.OrderBy(x => x.Id).Select((x, index) => new StockDocumentLine
            {
                LineNo = index + 1, ProductVariantId = x.ProductVariantId, ProductUnitConversionId = x.ProductUnitConversionId,
                UnitId = x.UnitId, UnitNameSnapshot = x.UnitNameSnapshot, ProductNameSnapshot = x.ProductNameSnapshot,
                Factor = x.Factor, Quantity = x.Quantity, BaseQuantity = x.BaseQuantity,
                UnitCost = x.UnitCost, UnitPriceBeforeVat = x.UnitPriceBeforeVat, UnitPriceAfterVat = x.UnitPriceAfterVat, LineTotal = x.LineTotal
            }).ToList()
        };
        db.Add(document); await db.SaveChangesAsync(); return document.Id;
    }

    private static async Task AddCommercialEvidence(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int id)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == id);
        document.TotalAmount = 54321; document.SubtotalBeforeVat = 50000; document.VatAmount = 4321;
        document.HasFreight = true; document.FreightTotal = 1000;
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        variant.CostPrice = 12345; variant.Price = 45678;
        foreach (var line in document.Lines)
        {
            line.UnitCost = 12340; line.UnitPriceBeforeVat = 12340; line.UnitPriceAfterVat = 13574;
            line.TaxRate = 10; line.VatAmount = 2468; line.LineTotal = 27148; line.FreightAllocation = 500;
        }
        var head = new InputInvoiceHead
        {
            StoreId = store.StoreId, SellerTaxCode = "0312770607", SellerName = "NCC XML bảo mật", BuyerTaxCode = "0101234567",
            InvoiceSeries = "C26SEC", InvoiceNumber = Guid.NewGuid().ToString("N")[..10], InvoiceDate = DateTime.Today,
            Details = [new InputInvoiceDetail { LineNo = 1, ItemName = "Sữa tươi XML", Quantity = 2, UnitPrice = 12340, LineAmount = 24680 }]
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head); db.Add(head); await db.SaveChangesAsync();
        db.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = id, InputInvoiceHeadId = head.Id });
        db.StockDocumentLineInputInvoiceMaps.Add(new StockDocumentLineInputInvoiceMap
        {
            StoreId = store.StoreId, StockDocumentId = id, StockDocumentLineId = document.Lines.OrderBy(x => x.Id).First().Id,
            InputInvoiceDetailId = head.Details.Single().Id, UseInputInvoice = true, AmountDifference = 987
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> MutationEvidence(AppDbContext db) => JsonSerializer.Serialize(new
    {
        Documents = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new
        { x.Id, x.StoreId, x.Status, x.SupplierId, x.PurchaseOrderId, x.ReceiptSource, x.TotalAmount, x.SubtotalBeforeVat, x.VatAmount,
            x.RowVersion, x.IsDeleted, x.UpdatedAtUtc, x.UpdatedBy }).ToListAsync(),
        Lines = await db.StockDocumentLines.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        ProvisionalItems = await db.StockDocumentProvisionalItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        ReceivingActions = await db.PurchaseReceivingActions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Plans = await db.PurchaseReceiptPricingPlans.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Variants = await db.ProductVariants.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CostPrice, x.Price, x.WholesalePrice, x.RowVersion }).ToListAsync(),
        Inventory = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Payables = await db.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Treasury = await db.TreasuryEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Audit = await db.PurchaseReceiptAuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
    });
}

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptSupplierCorrectionSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/document-actions";
    private static string Version(JsonElement data) => data.GetProperty("document").GetProperty("rowVersion").GetString()!;

    [Fact]
    public async Task Unsettled_correction_updates_payable_party_preserves_stock_and_freight_payment_and_requires_manager()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var manager = await app.LoginAsync(account);
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        using var wrongSource = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Approve));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var receiptId = await Approve(app, store, manager);
        var correctSupplier = await AddSupplier(app, store, "Nhà cung cấp đúng", "0312770607");
        var browserSupplier = await AddSupplier(app, store, "Nhà cung cấp sửa trên giao diện", "0312770608");
        var inactive = await AddSupplier(app, store, "Nhà cung cấp đã ngừng", "0312770609", false);
        int foreignSupplier;
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId)) foreignSupplier = (await db.Suppliers.SingleAsync()).Id;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var payable = await db.PurchasePayables.SingleAsync(x => x.StockDocumentId == receiptId && x.Type == PurchasePayableType.Freight);
            // A separate carrier's settled freight must not prevent correcting unsettled merchandise.
            payable.Status = PurchasePayableStatus.Paid;
            payable.PaidAtUtc = payable.RecognizedAtUtc.AddHours(1);
            db.TreasuryEntries.Add(new TreasuryEntry { StoreId = store.StoreId, ClientRequestId = Guid.NewGuid(),
                PurchasePayableId = payable.Id, Amount = -payable.Amount,
                OccurredAtUtc = payable.RecognizedAtUtc.AddHours(1), Name = "Phiếu chi đã ghi nhận", Fund = "bank" });
            await db.SaveChangesAsync();
        }
        var data = await manager.JsonAsync(HttpMethod.Get, Url(receiptId));
        Assert.True(data.GetProperty("canCorrectSupplier").GetBoolean());
        Assert.False((await employee.JsonAsync(HttpMethod.Get, Url(receiptId))).GetProperty("canCorrectSupplier").GetBoolean());
        var body = new { rowVersion = Version(data), supplierId = correctSupplier, reason = "Chọn nhầm nhà cung cấp khi duyệt" };
        foreach (var client in new[] { employee, wrongSource })
        {
            using var denied = await client.Http.PostAsJsonAsync(Url(receiptId) + "/correct-supplier", body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var denied = await foreign.Http.PostAsJsonAsync(Url(receiptId) + "/correct-supplier", body)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        foreach (var badId in new[] { 0, inactive, foreignSupplier, data.GetProperty("document").GetProperty("supplierId").GetInt32() })
        {
            using var bad = await manager.Http.PostAsJsonAsync(Url(receiptId) + "/correct-supplier", new { rowVersion = Version(data), supplierId = badId, body.reason });
            Assert.Contains(bad.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        }
        foreach (var reason in new[] { "", "   ", new string('a', 1001) })
        {
            using var bad = await manager.Http.PostAsJsonAsync(Url(receiptId) + "/correct-supplier", new { rowVersion = Version(data), supplierId = correctSupplier, reason });
            Assert.Contains(bad.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        }
        string before;
        byte[] payableVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            before = await UnchangedEvidence(db, receiptId);
            payableVersion = (await db.PurchasePayables.SingleAsync(x => x.StockDocumentId == receiptId && x.Type == PurchasePayableType.Merchandise)).RowVersion;
        }
        await manager.JsonAsync(HttpMethod.Post, Url(receiptId) + "/correct-supplier", body);
        using (var retry = await manager.Http.PostAsJsonAsync(Url(receiptId) + "/correct-supplier", body)) Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var latest = await manager.JsonAsync(HttpMethod.Get, Url(receiptId));
        Assert.NotEqual(Version(data), Version(latest));
        Assert.Equal(correctSupplier, latest.GetProperty("document").GetProperty("supplierId").GetInt32());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(before, await UnchangedEvidence(db, receiptId));
            var merchandise = await db.PurchasePayables.SingleAsync(x => x.StockDocumentId == receiptId && x.Type == PurchasePayableType.Merchandise);
            Assert.Equal(correctSupplier, merchandise.SupplierId);
            Assert.Equal("Nhà cung cấp đúng", merchandise.PayeeName);
            Assert.NotEqual(Convert.ToBase64String(payableVersion), Convert.ToBase64String(merchandise.RowVersion));
            var audit = await db.PurchaseReceiptAuditEvents.SingleAsync(x => x.StockDocumentId == receiptId && x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected);
            Assert.Equal(account.UserId, audit.ActorUserId); Assert.Equal(body.reason, audit.Reason);
            using var oldValues = JsonDocument.Parse(audit.OldValuesJson);
            using var newValues = JsonDocument.Parse(audit.NewValuesJson);
            Assert.Equal(data.GetProperty("document").GetProperty("supplierId").GetInt32(), oldValues.RootElement.GetProperty("SupplierId").GetInt32());
            Assert.Equal(correctSupplier, newValues.RootElement.GetProperty("SupplierId").GetInt32());
        }
        // The browser also verifies cancel, mandatory reason and the select staying locked after approval.
        await RunBrowser(app, account, receiptId, browserSupplier);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await UnchangedEvidence(final, receiptId));
        Assert.Equal(browserSupplier, (await final.StockDocuments.SingleAsync(x => x.Id == receiptId)).SupplierId);
        Assert.Equal(browserSupplier, (await final.PurchasePayables.SingleAsync(x => x.StockDocumentId == receiptId && x.Type == PurchasePayableType.Merchandise)).SupplierId);
        Assert.Equal(2, await final.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == receiptId && x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected));
    }

    [Fact]
    public async Task Pending_receipt_and_XML_or_purchase_order_conflicts_reject_atomically_but_matching_XML_allows_correction()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var inventoryManager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var supplierId = await AddSupplier(app, store, "Nhà cung cấp trên XML", "0312770607");
        var data = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.False(data.GetProperty("canCorrectSupplier").GetBoolean());
        using (var bad = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + "/correct-supplier", new { supplierId, reason = "Chưa duyệt", rowVersion = Version(data) }))
            Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        await ApproveExisting(app, store, manager, seed.ReceiptId);
        int headId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            var head = new InputInvoiceHead { StoreId = store.StoreId, SellerName = "NCC XML", SellerTaxCode = "0312770607",
                BuyerTaxCode = "0101234567", InvoiceNumber = "SUPPLIER-TEST", InvoiceSeries = "C26TEST", InvoiceDate = DateTime.Today,
                ResolvedSupplierId = receipt.SupplierId, SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head); db.Add(head); await db.SaveChangesAsync(); headId = head.Id;
            db.Add(new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = head.Id });
            await db.SaveChangesAsync();
        }
        data = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        Assert.True(data.GetProperty("document").GetProperty("hasLinkedInvoice").GetBoolean());
        var body = new { supplierId, reason = "Sửa theo hóa đơn nhà cung cấp", rowVersion = Version(data) };
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await UnchangedEvidence(db, seed.ReceiptId);
        using (var mismatch = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + "/correct-supplier", body))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Contains("XML", await mismatch.Content.ReadAsStringAsync());
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(before, await UnchangedEvidence(db, seed.ReceiptId));
            Assert.Empty(await db.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected).ToListAsync());
            // Legacy receipt supplier was wrong, while the XML has already been resolved to the correct supplier.
            (await db.InputInvoiceHeads.SingleAsync(x => x.Id == headId)).ResolvedSupplierId = supplierId;
            await db.SaveChangesAsync();
        }
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + "/correct-supplier", body);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(before, await UnchangedEvidence(db, seed.ReceiptId));
            var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            var po = new PurchaseOrder { StoreId = store.StoreId, OrderNumber = "PO-SUPPLIER-CORRECTION", SupplierId = supplierId,
                ExpectedWarehouseId = store.WarehouseId, LegalEntityId = receipt.ConfirmedLegalEntityId!.Value };
            db.Add(po); await db.SaveChangesAsync(); receipt.PurchaseOrderId = po.Id; receipt.ReceiptSource = PurchaseReceiptSource.PurchaseOrder;
            await db.SaveChangesAsync();
        }
        var other = await AddSupplier(app, store, "Khác đơn mua", "0312770608");
        data = await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId));
        var poBody = new { supplierId = other, reason = "Không được sửa lệch PO", rowVersion = Version(data) };
        using (var denied = await inventoryManager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + "/correct-supplier", poBody)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await manager.Http.PostAsJsonAsync(Url(seed.ReceiptId) + "/correct-supplier", poBody))
        {
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            Assert.Contains("đơn mua", await denied.Content.ReadAsStringAsync());
        }
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(supplierId, (await final.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).SupplierId);
        Assert.Equal(1, await final.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected));
    }

    [Fact]
    public async Task Competing_corrections_accept_one_version_and_keep_receipt_payable_and_audit_in_sync()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var id = await Approve(app, store, manager);
        var a = await AddSupplier(app, store, "NCC A", "0312770607");
        var b = await AddSupplier(app, store, "NCC B", "0312770608");
        var version = Version(await manager.JsonAsync(HttpMethod.Get, Url(id)));
        var responses = await Task.WhenAll(new[] { a, b }.Select(supplierId => manager.Http.PostAsJsonAsync(Url(id) + "/correct-supplier",
            new { supplierId, rowVersion = version, reason = "Hai quản lý sửa cùng lúc" })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.SingleAsync(x => x.Id == id);
        var payable = await db.PurchasePayables.SingleAsync(x => x.StockDocumentId == id && x.Type == PurchasePayableType.Merchandise);
        Assert.Equal(receipt.SupplierId, payable.SupplierId); Assert.Equal(receipt.MerchandisePayeeName, payable.PayeeName);
        Assert.Single(await db.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == id && x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected).ToListAsync());
    }

    [Theory]
    [InlineData("legacy-header")]
    [InlineData("legacy-payable")]
    [InlineData("paid-date")]
    [InlineData("partial")]
    [InlineData("paid")]
    [InlineData("reversed")]
    [InlineData("archived-history")]
    public async Task Settlement_or_any_payment_history_blocks_correction_and_preserves_every_financial_record(string scenario)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var id = await Approve(app, store, manager);
        var supplierId = await AddSupplier(app, store, "Nhà cung cấp thay thế", "0312770607");
        PurchasePayable payable;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            payable = await db.PurchasePayables.AsNoTracking().SingleAsync(x => x.StockDocumentId == id && x.Type == PurchasePayableType.Merchandise);

        if (scenario is "legacy-header" or "legacy-payable" or "paid-date")
        {
            // Legacy flags are deliberately seeded after a valid unpaid approval; new approvals
            // can no longer manufacture an unsupported paid state.
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            if (scenario == "legacy-header")
                (await db.StockDocuments.SingleAsync(x => x.Id == id)).IsMerchandisePaid = true;
            else
            {
                var legacy = await db.PurchasePayables.SingleAsync(x => x.Id == payable.Id);
                if (scenario == "legacy-payable") legacy.Status = PurchasePayableStatus.Paid;
                legacy.PaidAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        else
        {
            var payment = await manager.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto
            {
                ClientRequestId = Guid.NewGuid(), Date = DateTime.UtcNow.AddHours(7).Date,
                Fund = "bank", Amount = scenario == "paid" ? -payable.Amount : -1000,
                Name = "Chi tiền cho nhà cung cấp gốc", PurchasePayableId = payable.Id,
                SourceRowVersion = Convert.ToBase64String(payable.RowVersion)
            });
            var paymentId = payment.GetProperty("id").GetInt32();
            if (scenario == "reversed")
            {
                string version;
                await using (var db = app.Database.CreateTenantContext(store.StoreId))
                    version = Convert.ToBase64String((await db.TreasuryEntries.SingleAsync(x => x.Id == paymentId)).RowVersion);
                await manager.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{paymentId}/reverse", new TreasuryReverseDto
                {
                    ClientRequestId = Guid.NewGuid(), Date = DateTime.UtcNow.AddHours(7).Date,
                    RowVersion = version, Reason = "Đảo phiếu nhưng không được viết lại người nhận cũ"
                });
            }
            else if (scenario == "archived-history")
            {
                await using var db = app.Database.CreateTenantContext(store.StoreId);
                (await db.TreasuryEntries.SingleAsync(x => x.Id == paymentId)).IsDeleted = true;
                await db.SaveChangesAsync();
            }
        }

        var state = await manager.JsonAsync(HttpMethod.Get, Url(id));
        Assert.False(state.GetProperty("canCorrectSupplier").GetBoolean());
        Assert.False(state.GetProperty("document").GetProperty("canCorrectSupplier").GetBoolean());
        Assert.Contains("thanh toán", state.GetProperty("document").GetProperty("supplierCorrectionBlockedReason").GetString());
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) before = await RejectedEvidence(db, id);
        using var rejected = await manager.Http.PostAsJsonAsync(Url(id) + "/correct-supplier", new
        {
            rowVersion = Version(state), supplierId, reason = "Không được đổi bên nhận sau khi có thanh toán"
        });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("thanh toán", await rejected.Content.ReadAsStringAsync());
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(before, await RejectedEvidence(final, id));
        Assert.Empty(await final.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == id &&
            x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected).ToListAsync());
    }

    [Fact]
    public async Task In_flight_treasury_payment_serializes_before_supplier_correction_and_blocks_party_rewrite()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var id = await Approve(app, store, manager);
        var supplierId = await AddSupplier(app, store, "NCC không được thay trong lúc chi tiền", "0312770607");
        var state = await manager.JsonAsync(HttpMethod.Get, Url(id));
        Assert.True(state.GetProperty("canCorrectSupplier").GetBoolean());
        string originalParty;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            originalParty = await SupplierPartyEvidence(db, id);

        Task<HttpResponseMessage> correction;
        await using (var paymentDb = app.Database.CreateTenantContext(store.StoreId))
        {
            await using var transaction = await paymentDb.Database.BeginTransactionAsync();
            // Use TreasuryService's exact transaction-owned lock order, and leave the payment
            // uncommitted while the HTTP correction is waiting for that same boundary.
            var resource = $"treasury:{store.StoreId}";
            await paymentDb.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r<0 THROW 51000,'Treasury is busy.',1;");
            correction = manager.Http.PostAsJsonAsync(Url(id) + "/correct-supplier", new
            {
                rowVersion = Version(state), supplierId, reason = "Sửa đồng thời với thanh toán"
            });
            await Task.Delay(250);
            Assert.False(correction.IsCompleted);
            var payable = await paymentDb.PurchasePayables.SingleAsync(x => x.StockDocumentId == id && x.Type == PurchasePayableType.Merchandise);
            paymentDb.TreasuryEntries.Add(new TreasuryEntry
            {
                StoreId = store.StoreId, ClientRequestId = Guid.NewGuid(), Fund = "bank", Amount = -1000,
                OccurredAtUtc = DateTime.UtcNow, Name = "Chi một phần cho nhà cung cấp gốc",
                PurchasePayableId = payable.Id
            });
            payable.UpdatedAtUtc = DateTime.UtcNow;
            await paymentDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        using var rejected = await correction;
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("thanh toán", await rejected.Content.ReadAsStringAsync());
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(originalParty, await SupplierPartyEvidence(final, id));
        Assert.Single(await final.TreasuryEntries.Where(x => x.PurchasePayableId != null).ToListAsync());
        Assert.Empty(await final.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == id &&
            x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_real_payment_and_supplier_correction_accept_only_one_compatible_payable_version()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var id = await Approve(app, store, manager);
        var state = await manager.JsonAsync(HttpMethod.Get, Url(id));
        var supplierId = await AddSupplier(app, store, "NCC mới khi chưa thanh toán", "0312770607");
        PurchasePayable payable;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            payable = await db.PurchasePayables.AsNoTracking().SingleAsync(x => x.StockDocumentId == id && x.Type == PurchasePayableType.Merchandise);
        var originalSupplier = state.GetProperty("document").GetProperty("supplierId").GetInt32();
        var responses = await Task.WhenAll(
            manager.Http.PostAsJsonAsync(Url(id) + "/correct-supplier", new
            {
                rowVersion = Version(state), supplierId, reason = "Sửa NCC đồng thời với phiếu chi thật"
            }),
            manager.Http.PostAsJsonAsync("/admin/reports/treasury/entries", new TreasuryWriteDto
            {
                ClientRequestId = Guid.NewGuid(), Date = DateTime.UtcNow.AddHours(7).Date,
                Fund = "bank", Amount = -1000, Name = "Chi cho nhà cung cấp đã xem",
                PurchasePayableId = payable.Id, SourceRowVersion = Convert.ToBase64String(payable.RowVersion)
            }));
        bool correctionAccepted;
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            correctionAccepted = responses[0].IsSuccessStatusCode;
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        var document = await final.StockDocuments.SingleAsync(x => x.Id == id);
        var latestPayable = await final.PurchasePayables.SingleAsync(x => x.Id == payable.Id);
        Assert.Equal(correctionAccepted ? supplierId : originalSupplier, document.SupplierId);
        Assert.Equal(document.SupplierId, latestPayable.SupplierId);
        Assert.Equal(document.MerchandisePayeeName, latestPayable.PayeeName);
        Assert.Equal(correctionAccepted ? 0 : 1, await final.TreasuryEntries.CountAsync(x => x.PurchasePayableId == payable.Id));
        Assert.Equal(correctionAccepted ? 1 : 0, await final.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == id &&
            x.EventType == PurchaseReceiptAuditEventType.ReceiptSupplierCorrected));
    }

    private static async Task<string> SupplierPartyEvidence(AppDbContext db, int id) => JsonSerializer.Serialize(new
    {
        Document = await db.StockDocuments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.SupplierId, x.MerchandisePayeeName, x.RowVersion }).SingleAsync(),
        Payables = await db.PurchasePayables.IgnoreQueryFilters().AsNoTracking().Where(x => x.StockDocumentId == id)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.SupplierId, x.PayeeName }).ToListAsync()
    });

    private static async Task<string> RejectedEvidence(AppDbContext db, int id) => JsonSerializer.Serialize(new
    {
        ExistingEvidence = await UnchangedEvidence(db, id),
        Party = await SupplierPartyEvidence(db, id),
        Payables = await db.PurchasePayables.IgnoreQueryFilters().AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id).ToListAsync(),
        Treasury = await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Audit = await db.PurchaseReceiptAuditEvents.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id).ToListAsync()
    });

    private static async Task<int> AddSupplier(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, string name, string taxCode, bool active = true)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var supplier = new Supplier { StoreId = store.StoreId, Name = name, Code = "S-" + taxCode, TaxCode = taxCode, IsActive = active };
        db.Add(supplier); await db.SaveChangesAsync(); return supplier.Id;
    }
    private static async Task<int> Approve(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Client manager)
    {
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await ApproveExisting(app, store, manager, seed.ReceiptId); return seed.ReceiptId;
    }
    private static async Task ApproveExisting(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Client manager, int receiptId)
    {
        ApprovePurchaseReceiptCommercialRequest request;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
            doc.Status = StockDocumentStatus.PendingApproval;
            doc.IsMerchandisePaid = false;
            await db.SaveChangesAsync();
            request = new() { RowVersion = Convert.ToBase64String(doc.RowVersion), SupplierId = doc.SupplierId,
                IsMerchandisePaid = false, WaitForInputInvoice = true, HasFreight = true, FreightTotal = 1000,
                FreightPayeeName = "Đơn vị vận chuyển riêng", IsFreightPaid = false,
                Lines = doc.Lines.Select(x => new PurchaseReceiptFinancialLineInputDto { StockDocumentLineId = x.Id, UnitPriceBeforeVat = 10000 }).ToList() };
        }
        await manager.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{receiptId}/approve-commercial", request);
    }
    private static async Task<string> UnchangedEvidence(AppDbContext db, int id) => JsonSerializer.Serialize(new
    {
        Header = await db.StockDocuments.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.DocumentNo, x.DocumentTitle, x.WarehouseId,
            x.Status, x.DocumentDate, x.HasVat, x.IncludeVatInInventoryCost, x.SubtotalBeforeVat, x.VatAmount, x.TotalAmount,
            x.HasFreight, x.FreightTotal, x.FreightPayeeName, x.CapitalizeFreightInInventoryCost, x.IsFreightPaid, x.IsMerchandisePaid,
            x.WaitForInputInvoice, x.ApprovedAtUtc, x.ApprovedByUserId, x.ConfirmedAtUtc, x.ConfirmedByUserId, x.ConfirmedLegalEntityId }).SingleAsync(),
        Lines = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id).ToListAsync(),
        Inventory = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Valuations = await db.InventoryValuationEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Layers = await db.InventoryCostLayers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        InvoiceStock = await db.InvoiceInputStockSupplementalMovements.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Payables = await db.PurchasePayables.AsNoTracking().Where(x => x.StockDocumentId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Type, x.SourceKey, x.Amount, x.Status, x.PaidAtUtc, x.RecognizedAtUtc, x.DueDate, x.Note }).ToListAsync(),
        Freight = await db.PurchasePayables.AsNoTracking().Where(x => x.StockDocumentId == id && x.Type == PurchasePayableType.Freight).ToListAsync(),
        Treasury = await db.TreasuryEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
    });

    private static async Task RunBrowser(FullApplicationFixture app, FullApplicationFixture.Account account, int receiptId, int supplierId)
    {
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/receipt-supplier-correction.browser.cjs"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{account.Store.Host}:{app.Address.Port}",
            user = account.Name, password = account.Password, terminalId = account.Store.TerminalId, receiptId, supplierId,
            evidenceRoot = Path.Combine(root, "Logs/receipt-supplier-correction-20261009") }));
        process.StandardInput.Close(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}

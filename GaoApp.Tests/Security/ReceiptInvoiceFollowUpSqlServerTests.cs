using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptInvoiceFollowUpSqlServerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approval_posts_now_retry_preserves_choice_and_end_waiting_never_reposts(bool wait)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        ApprovePurchaseReceiptCommercialRequest request;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            doc.Status = StockDocumentStatus.PendingApproval;
            await db.SaveChangesAsync();
            request = new()
            {
                RowVersion = Convert.ToBase64String(doc.RowVersion), WaitForInputInvoice = wait,
                SupplierId = doc.SupplierId, IsMerchandisePaid = false,
                Lines = doc.Lines.Select(x => new PurchaseReceiptFinancialLineInputDto
                    { StockDocumentLineId = x.Id, UnitPriceBeforeVat = 10000 }).ToList()
            };
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var url = $"/admin/api/stock-documents/{seed.ReceiptId}/invoice-follow-up";
        var approveUrl = $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial";
        var legacy = await manager.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("Unclassified", legacy.GetProperty("state").GetString());
        await manager.JsonAsync(HttpMethod.Post, approveUrl, request);
        var context = await manager.JsonAsync(HttpMethod.Get, url);
        Assert.Equal(wait ? "Waiting" : "NotExpected", context.GetProperty("state").GetString());
        Assert.True(context.GetProperty("isConfirmed").GetBoolean());
        request.WaitForInputInvoice = !wait;
        await manager.JsonAsync(HttpMethod.Post, approveUrl, request);
        Assert.Equal(wait ? "Waiting" : "NotExpected", (await manager.JsonAsync(HttpMethod.Get, url)).GetProperty("state").GetString());

        await using var before = app.Database.CreateTenantContext(store.StoreId);
        var transactions = await before.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToListAsync();
        Assert.Contains(transactions, x => x.QuantityChange > 0 && x.TotalCost > 0);
        var receiptTransactions = await before.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == InventoryReferenceType.StockDocument
                && x.ReferenceId == seed.ReceiptId.ToString()
                && x.TransactionType == InventoryTransactionType.PurchaseReceipt && x.QuantityChange > 0)
            .Select(x => new { x.QuantityChange, x.TotalCost }).ToListAsync();
        Assert.NotEmpty(receiptTransactions);
        var payableCount = await before.PurchasePayables.CountAsync(x => x.StockDocumentId == seed.ReceiptId);
        Assert.True(payableCount > 0);
        var list = await new StockDocumentRepository(before).GetReceiptListAsync();
        Assert.Equal(wait ? "Waiting" : "NotExpected", list.Single(x => x.Id == seed.ReceiptId).InvoiceFollowUp);

        var body = new { rowVersion = context.GetProperty("rowVersion").GetString(), reason = "Nhà cung cấp xác nhận không gửi hóa đơn" };
        using (var denied = await viewer.Http.PostAsJsonAsync(url + "/end-waiting", body)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.GetAsync(url)) Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        if (wait)
        {
            using (var stale = await manager.Http.PostAsJsonAsync(url + "/end-waiting", new { rowVersion = request.RowVersion, reason = "test" }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var ended = await manager.JsonAsync(HttpMethod.Post, url + "/end-waiting", body);
            Assert.Equal("NotExpected", ended.GetProperty("state").GetString());
        }
        using (var repeated = await manager.Http.PostAsJsonAsync(url + "/end-waiting", body)) Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        await using var after = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(transactions, await after.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToListAsync());
        Assert.Equal(payableCount, await after.PurchasePayables.CountAsync(x => x.StockDocumentId == seed.ReceiptId));
        Assert.Equal(wait ? 1 : 0, await after.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceWaitingEnded));
        Assert.Equal(1, await after.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.CommercialApprovalConfirmed));

        if (!wait) return;
        // A late invoice can contain several receipts and unidentified lines. Reviewing it
        // completes follow-up only; it must never claim a numerical match or repost stock.
        int mapId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            var head = new InputInvoiceHead { StoreId = store.StoreId, SellerName = "Supplier", SellerTaxCode = "0312770607",
                BuyerTaxCode = "0101234567", InvoiceSeries = "C26TEST", InvoiceNumber = "9001", InvoiceDate = DateTime.Today,
                ResolvedSupplierId = doc.SupplierId, SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head);
            db.Add(head); await db.SaveChangesAsync();
            var map = new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = doc.Id, InputInvoiceHeadId = head.Id };
            db.Add(map); await db.SaveChangesAsync(); mapId = map.Id;
            db.Add(new StockDocumentInputInvoiceReconciliation { StoreId = store.StoreId, StockDocumentId = doc.Id,
                StockDocumentInputInvoiceMapId = map.Id, InputInvoiceHeadId = head.Id, EvidenceFingerprint = "evidence-1",
                OverallState = InputInvoiceReconciliationState.Incomplete, ReceiptGoodsTotal = 1688899, XmlPaymentAmount = 4580891,
                UnmatchedDetailCount = 16, LastCalculatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var linked = await manager.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("NeedsReview", linked.GetProperty("state").GetString());
        var review = new ReviewReceiptInvoiceRequest { RowVersion = linked.GetProperty("rowVersion").GetString()!, MapId = mapId,
            EvidenceFingerprint = "evidence-1", Reason = "Hóa đơn chung cho nhiều phiếu; đã kiểm tra phần hàng của phiếu này" };
        using (var denied = await viewer.Http.PostAsJsonAsync(url + "/review", review)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.PostAsJsonAsync(url + "/review", review)) Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        review.RowVersion = "stale";
        using (var stale = await manager.Http.PostAsJsonAsync(url + "/review", review)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        review.RowVersion = linked.GetProperty("rowVersion").GetString()!;
        review.MapId++;
        using (var stale = await manager.Http.PostAsJsonAsync(url + "/review", review)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        review.MapId = mapId;
        review.EvidenceFingerprint = "stale";
        using (var stale = await manager.Http.PostAsJsonAsync(url + "/review", review)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        review.EvidenceFingerprint = "evidence-1";
        var reviewed = await manager.JsonAsync(HttpMethod.Post, url + "/review", review);
        Assert.Equal("Reviewed", reviewed.GetProperty("state").GetString());
        Assert.Equal(review.Reason, reviewed.GetProperty("reviewReason").GetString());
        await manager.JsonAsync(HttpMethod.Post, url + "/review", review); // Safe retry.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal("Reviewed", (await new StockDocumentRepository(db).GetReceiptListAsync()).Single(x => x.Id == seed.ReceiptId).InvoiceFollowUp);
            Assert.Equal(1, await db.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed));
            Assert.Equal(transactions, await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuantityChange, x.TotalCost }).ToListAsync());
            Assert.Equal(payableCount, await db.PurchasePayables.CountAsync(x => x.StockDocumentId == seed.ReceiptId));
            var reconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync(x => x.StockDocumentId == seed.ReceiptId);
            Assert.Equal(InputInvoiceReconciliationState.Incomplete, reconciliation.OverallState);
            Assert.Equal(16, reconciliation.UnmatchedDetailCount);
            var invoiceStock = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(store.StoreId);
            var receiptStock = invoiceStock.Where(x => x.StockDocumentId == seed.ReceiptId).ToList();
            Assert.Equal(receiptTransactions.Sum(x => x.QuantityChange), receiptStock.Sum(x => x.Change));
            Assert.Equal(receiptTransactions.Sum(x => x.TotalCost), receiptStock.Sum(x => x.TotalCost));
            Assert.All(receiptStock, x => Assert.StartsWith("reviewed-receipt-", x.Key));
            reconciliation.EvidenceFingerprint = "evidence-2";
            await db.SaveChangesAsync();
            Assert.Equal("NeedsReview", (await new StockDocumentRepository(db).GetReceiptListAsync()).Single(x => x.Id == seed.ReceiptId).InvoiceFollowUp);
            invoiceStock = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(store.StoreId);
            Assert.DoesNotContain(invoiceStock, x => x.StockDocumentId == seed.ReceiptId);
        }
        Assert.Equal("NeedsReview", (await manager.JsonAsync(HttpMethod.Get, url)).GetProperty("state").GetString());
        using (var stale = await manager.Http.PostAsJsonAsync(url + "/review", review)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        review.EvidenceFingerprint = "evidence-2";
        await manager.JsonAsync(HttpMethod.Post, url + "/review", review);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var invoiceStock = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(store.StoreId);
            Assert.Equal(receiptTransactions.Sum(x => x.QuantityChange),
                invoiceStock.Where(x => x.StockDocumentId == seed.ReceiptId).Sum(x => x.Change));
        }
        var auditTenant = new TenantContext(); auditTenant.SetStore(store.StoreId, "test");
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(app.Database.ConnectionString).Options, auditTenant, new AuditUser()))
        {
            // A restored link may reuse its map ID. The later linking event invalidates the old review too.
            db.PurchaseReceiptAuditEvents.Add(new() { StoreId = store.StoreId, StockDocumentId = seed.ReceiptId,
                EventType = PurchaseReceiptAuditEventType.InputInvoiceRelinked, IsSuccess = true, OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = (await db.PurchaseReceiptAuditEvents.FirstAsync(x => x.StockDocumentId == seed.ReceiptId)).ActorUserId });
            await db.SaveChangesAsync();
            Assert.Equal("NeedsReview", (await new StockDocumentRepository(db).GetReceiptListAsync()).Single(x => x.Id == seed.ReceiptId).InvoiceFollowUp);
            Assert.DoesNotContain(await new InvoiceInputStockReadRepository(db).GetMovementsAsync(store.StoreId),
                x => x.StockDocumentId == seed.ReceiptId);
        }
        Assert.Equal("NeedsReview", (await manager.JsonAsync(HttpMethod.Get, url)).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("[]")]
    [InlineData("{\"MapId\":\"1\"}")]
    [InlineData("{\"MapId\":2,\"EvidenceFingerprint\":\"hash\"}")]
    [InlineData("{\"MapId\":1,\"EvidenceFingerprint\":\"changed\"}")]
    public void Invalid_or_stale_review_does_not_complete_follow_up(string evidence)
        => Assert.False(ReceiptInvoiceFollowUp.IsReviewCurrent(evidence, 1, "hash"));

    private sealed class AuditUser : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "review-fixture";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    [Theory]
    [InlineData(null, false, null, "Unclassified")]
    [InlineData(true, false, null, "Waiting")]
    [InlineData(false, false, null, "NotExpected")]
    [InlineData(true, true, null, "NeedsReview")]
    [InlineData(false, true, InputInvoiceReconciliationState.Mismatch, "NeedsReview")]
    [InlineData(null, true, InputInvoiceReconciliationState.Matched, "Complete")]
    [InlineData(true, true, InputInvoiceReconciliationState.AcceptedMismatch, "Complete")]
    public void Invoice_status_is_independent_of_posting(bool? wait, bool linked, InputInvoiceReconciliationState? state, string expected)
        => Assert.Equal(expected, ReceiptInvoiceFollowUp.Resolve(wait, linked, state));
}

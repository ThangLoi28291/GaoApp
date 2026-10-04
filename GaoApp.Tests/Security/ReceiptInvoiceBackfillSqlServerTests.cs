using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Services.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptInvoiceBackfillSqlServerTests
{
    private const string Endpoint = "/admin/api/receipt-invoice-backfill";

    [Fact]
    public void Date_filter_uses_Vietnam_link_date_including_the_entire_end_date()
    {
        var range = ReceiptInvoiceBackfillService.GetUtcRange(new()
            { LinkedFromDate = new(2026, 9, 26), LinkedToDate = new(2026, 10, 4) });
        Assert.Equal(new DateTime(2026, 9, 25, 17, 0, 0, DateTimeKind.Utc), range.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc), range.ToUtcExclusive);
        Assert.Null(ReceiptInvoiceBackfillService.GetUtcRange(new()).ToUtcExclusive);
        Assert.Throws<BusinessRuleException>(() => ReceiptInvoiceBackfillService.GetUtcRange(new()
            { LinkedFromDate = new(2026, 9, 26), LinkedToDate = new(2026, 9, 25) }));
        Assert.Throws<BusinessRuleException>(() => ReceiptInvoiceBackfillService.GetUtcRange(new() { PageSize = 51 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_batch_initializes_missing_evidence_rejects_stale_preview_and_never_reposts(bool emptyFingerprint)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var managerAccount = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve, PermissionCodes.Inventory.Transaction.View);
        using var manager = await app.LoginAsync(managerAccount);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var purchaseOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Approve));
        ApprovePurchaseReceiptCommercialRequest approval;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            receipt.Status = StockDocumentStatus.PendingApproval; await db.SaveChangesAsync();
            approval = new() { RowVersion = Convert.ToBase64String(receipt.RowVersion), SupplierId = receipt.SupplierId,
                IsMerchandisePaid = false, Lines = receipt.Lines.Select(x => new PurchaseReceiptFinancialLineInputDto
                    { StockDocumentLineId = x.Id, UnitPriceBeforeVat = 10000 }).ToList() };
        }
        await manager.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", approval);
        int mapId, beforeCutoffId, afterEndId, noLedgerId, draftId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            var head = new InputInvoiceHead { StoreId = store.StoreId, SellerName = "Supplier", SellerTaxCode = "0312770607",
                BuyerTaxCode = "0101234567", InvoiceSeries = "C26BF", InvoiceNumber = "1", InvoiceDate = new(2026, 3, 24),
                ResolvedSupplierId = receipt.SupplierId, SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head);
            db.Add(head); await db.SaveChangesAsync();
            var map = new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = head.Id };
            db.Add(map); await db.SaveChangesAsync(); mapId = map.Id;
            map.CreatedAtUtc = new(2026, 9, 23); await db.SaveChangesAsync();
            // Historical fixture evidence: runtime audit writes always use the current time.
            var historicalLinkTime = new DateTime(2026, 9, 25, 17, 0, 0, DateTimeKind.Utc);
            var linkValues = JsonSerializer.Serialize(new { InputInvoiceHeadId = head.Id });
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [PurchaseReceiptAuditEvents] ([StoreId],[StockDocumentId],[EventType],[ActorUserId],[OccurredAtUtc],[IsSuccess],[ChangedFieldsJson],[OldValuesJson],[NewValuesJson]) VALUES ({store.StoreId},{receipt.Id},{28},{managerAccount.UserId},{historicalLinkTime},{true},{"[]"},{"{}"},{linkValues})");
            if (emptyFingerprint)
            {
                db.Add(new StockDocumentInputInvoiceReconciliation { StoreId = store.StoreId, StockDocumentId = receipt.Id,
                    StockDocumentInputInvoiceMapId = map.Id, InputInvoiceHeadId = head.Id, EvidenceFingerprint = "",
                    OverallState = InputInvoiceReconciliationState.Incomplete, LastCalculatedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            async Task<int> Extra(DateTime linkedAt, StockDocumentStatus status = StockDocumentStatus.Confirmed)
            {
                var other = new StockDocument { StoreId = store.StoreId, Type = StockDocumentType.Receipt, Status = status,
                    ReceiptSource = PurchaseReceiptSource.Direct, SupplierId = receipt.SupplierId, WarehouseId = store.WarehouseId,
                    DocumentNo = "BF-" + Guid.NewGuid().ToString("N")[..10], ApprovedAtUtc = new(2026, 10, 2),
                    ConfirmedLegalEntityId = status == StockDocumentStatus.Confirmed ? receipt.ConfirmedLegalEntityId : null,
                    DocumentDate = new(2026, 10, 1) };
                db.Add(other); await db.SaveChangesAsync();
                var association = new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = other.Id, InputInvoiceHeadId = head.Id };
                db.Add(association); await db.SaveChangesAsync(); association.CreatedAtUtc = linkedAt; await db.SaveChangesAsync();
                return other.Id;
            }
            beforeCutoffId = await Extra(new(2026, 9, 25, 16, 59, 59));
            afterEndId = await Extra(new(2026, 9, 26, 17, 0, 0));
            noLedgerId = await Extra(new(2026, 9, 26, 16, 59, 59));
            draftId = await Extra(new(2026, 9, 25, 18, 0, 0), StockDocumentStatus.Draft);
        }
        const string scanUrl = Endpoint + "?linkedFromDate=2026-09-26&linkedToDate=2026-09-26";
        using (var denied = await viewer.Http.GetAsync(scanUrl)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var foreignPreview = await foreign.JsonAsync(HttpMethod.Get, scanUrl);
        Assert.Equal(0, foreignPreview.GetProperty("totalItems").GetInt32());
        var purchasePreview = await purchaseOnly.JsonAsync(HttpMethod.Get, scanUrl);
        Assert.Equal(0, purchasePreview.GetProperty("totalItems").GetInt32());

        await using var before = app.Database.CreateTenantContext(store.StoreId);
        var transactions = await before.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost }).ToListAsync();
        var payables = await before.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync();
        var originalHeader = await before.StockDocuments.AsNoTracking().Where(x => x.Id == seed.ReceiptId)
            .Select(x => new { x.CreatedAtUtc, x.ApprovedAtUtc, x.ConfirmedAtUtc, x.DocumentDate, x.RowVersion }).SingleAsync();
        var auditCount = await before.PurchaseReceiptAuditEvents.CountAsync();
        var reconciliationCount = await before.StockDocumentInputInvoiceReconciliations.CountAsync();
        var preview = await manager.JsonAsync(HttpMethod.Get, scanUrl);
        Assert.Equal(2, preview.GetProperty("totalItems").GetInt32());
        var rows = preview.GetProperty("rows").EnumerateArray().ToArray();
        var ready = Assert.Single(rows, x => x.GetProperty("receiptId").GetInt32() == seed.ReceiptId);
        Assert.Equal("Ready", ready.GetProperty("status").GetString());
        Assert.Equal("Blocked", Assert.Single(rows, x => x.GetProperty("receiptId").GetInt32() == noLedgerId).GetProperty("status").GetString());
        Assert.DoesNotContain(rows, x => new[] { beforeCutoffId, afterEndId, draftId }.Contains(x.GetProperty("receiptId").GetInt32()));
        Assert.Equal(auditCount, await before.PurchaseReceiptAuditEvents.CountAsync());
        Assert.Equal(reconciliationCount, await before.StockDocumentInputInvoiceReconciliations.CountAsync());
        Assert.Equal(transactions, await before.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost }).ToListAsync());
        if (!emptyFingerprint) await CheckBrowserAsync(app, managerAccount, seed.ReceiptId);
        var body = new ReceiptInvoiceBackfillRequest { LinkedFromDate = new(2026, 9, 26), LinkedToDate = new(2026, 9, 26),
            Reason = "Đã kiểm tra phiếu cũ có XML", Items = [new() { ReceiptId = seed.ReceiptId, SnapshotHash = ready.GetProperty("snapshotHash").GetString()! }] };
        using (var denied = await viewer.Http.PostAsJsonAsync(Endpoint + "/confirm", body)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var noCsrf = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/confirm") { Content = JsonContent.Create(body) })
        {
            noCsrf.Headers.Add("RequestVerificationToken", "invalid");
            using var response = await manager.Http.SendAsync(noCsrf); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal("Blocked", (await foreign.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body))[0].GetProperty("status").GetString());
        Assert.Equal("Blocked", (await purchaseOnly.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body))[0].GetProperty("status").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.StockDocumentInputInvoiceMaps.SingleAsync(x => x.Id == mapId)).Note = "XML được cập nhật sau rà soát";
            await db.SaveChangesAsync();
        }
        Assert.Equal("Blocked", (await manager.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body))[0].GetProperty("status").GetString());
        Assert.Equal(reconciliationCount, await before.StockDocumentInputInvoiceReconciliations.CountAsync());
        Assert.False(await before.PurchaseReceiptAuditEvents.AnyAsync(x => x.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed));
        preview = await manager.JsonAsync(HttpMethod.Get, scanUrl);
        body.Items[0].SnapshotHash = preview.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("receiptId").GetInt32() == seed.ReceiptId).GetProperty("snapshotHash").GetString()!;
        var concurrent = await Task.WhenAll(manager.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body), manager.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body));
        Assert.Single(concurrent, x => x[0].GetProperty("status").GetString() == "Confirmed");
        Assert.Single(concurrent, x => x[0].GetProperty("status").GetString() == "Skipped");
        Assert.Equal("Skipped", (await manager.JsonAsync(HttpMethod.Post, Endpoint + "/confirm", body))[0].GetProperty("status").GetString());
        await using var after = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(1, await after.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == seed.ReceiptId && x.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed));
        Assert.Equal(transactions, await after.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost }).ToListAsync());
        Assert.Equal(payables, await after.PurchasePayables.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount }).ToListAsync());
        var header = await after.StockDocuments.AsNoTracking().Where(x => x.Id == seed.ReceiptId)
            .Select(x => new { x.CreatedAtUtc, x.ApprovedAtUtc, x.ConfirmedAtUtc, x.DocumentDate, x.RowVersion }).SingleAsync();
        Assert.Equal(originalHeader.CreatedAtUtc, header.CreatedAtUtc);
        Assert.Equal(originalHeader.ApprovedAtUtc, header.ApprovedAtUtc);
        Assert.Equal(originalHeader.ConfirmedAtUtc, header.ConfirmedAtUtc);
        Assert.Equal(originalHeader.DocumentDate, header.DocumentDate);
        Assert.Equal(originalHeader.RowVersion, header.RowVersion);
        var inbound = await after.InventoryTransactions.AsNoTracking().Where(x => x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == seed.ReceiptId.ToString()).ToListAsync();
        var documentary = (await new InvoiceInputStockReadRepository(after).GetMovementsAsync(store.StoreId)).Where(x => x.StockDocumentId == seed.ReceiptId).ToList();
        Assert.Equal(inbound.Count, documentary.Count);
        Assert.Equal(inbound.Sum(x => x.QuantityChange), documentary.Sum(x => x.Change));
        Assert.Equal(inbound.Sum(x => x.TotalCost), documentary.Sum(x => x.TotalCost));
        Assert.All(documentary, x => Assert.StartsWith("reviewed-receipt-", x.Key));
        preview = await manager.JsonAsync(HttpMethod.Get, scanUrl);
        Assert.Equal(1, preview.GetProperty("completeCount").GetInt32());
        Assert.Equal(0, preview.GetProperty("readyCount").GetInt32());
    }

    private static async Task CheckBrowserAsync(FullApplicationFixture app, FullApplicationFixture.Account account, int receiptId)
    {
        var root = FullApplicationFixture.SourceRoot();
        var evidence = Environment.GetEnvironmentVariable("GAOAPP_BACKFILL_EVIDENCE_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "GaoAppBackfillBrowser_" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "receipt-invoice-backfill.browser.cjs"));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{account.Store.Host}:{app.Address.Port}",
            user = account.Name, password = account.Password, terminalId = account.Store.TerminalId, receiptId, evidenceRoot = evidence }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}

using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
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
        var managerAccount = await app.AddAccountAsync(store, "*");
        const int receiptCount = 1000;
        var date = new DateTime(2026, 9, 28);
        int emptyId, specialId;
        Dictionary<int, int> currentMapIds;
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
            var receiptIds = receipts.Select(x => x.Id).ToArray();
            db.ChangeTracker.Clear();
            var currentHead = NewInvoice(store.StoreId, supplier.Id, "8001");
            var oldHead = NewInvoice(store.StoreId, supplier.Id, "8002");
            db.AddRange(currentHead, oldHead); await db.SaveChangesAsync();
            var oldMaps = receiptIds.Select(id => new StockDocumentInputInvoiceMap
                { StoreId = store.StoreId, StockDocumentId = id, InputInvoiceHeadId = oldHead.Id }).ToList();
            db.AddRange(oldMaps); await db.SaveChangesAsync();
            db.AddRange(oldMaps.Select(map => NewReconciliation(store.StoreId, map, "old-evidence", InputInvoiceReconciliationState.Matched)));
            foreach (var map in oldMaps) map.IsDeleted = true;
            await db.SaveChangesAsync();
            var currentMaps = receiptIds.Select(id => new StockDocumentInputInvoiceMap
                { StoreId = store.StoreId, StockDocumentId = id, InputInvoiceHeadId = currentHead.Id }).ToList();
            db.AddRange(currentMaps); await db.SaveChangesAsync();
            db.AddRange(currentMaps.Select(map => NewReconciliation(store.StoreId, map, $"list-{map.StockDocumentId}", InputInvoiceReconciliationState.Incomplete)));
            await db.SaveChangesAsync();
            currentMapIds = currentMaps.ToDictionary(x => x.StockDocumentId, x => x.Id);

            // Persisted historical fixture evidence: runtime audit writes intentionally
            // stamp the current actor/time. These inserts remain inside the GUID test DB.
            var historicalJson = JsonSerializer.Serialize(new { HistoricalPayload = new string('h', 2048) });
            var historicalTime = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
            var linkedType = (int)PurchaseReceiptAuditEventType.InputInvoiceLinked;
            const string emptyJson = "{}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [PurchaseReceiptAuditEvents]
                    ([StoreId],[StockDocumentId],[EventType],[ActorUserId],[OccurredAtUtc],[IsSuccess],[ChangedFieldsJson],[OldValuesJson],[NewValuesJson])
                SELECT {store.StoreId}, [d].[Id], {linkedType}, {managerAccount.UserId}, {historicalTime}, 1, N'[]', {emptyJson}, {historicalJson}
                FROM [StockDocument] AS [d]
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) AS [history]([Ordinal])
                WHERE [d].[StoreId] = {store.StoreId} AND [d].[IsDeleted] = 0
                    AND [d].[DocumentNo] LIKE N'LIST-[0-9]%'
                """);
            var reviewType = (int)PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed;
            const string reviewJsonPrefix = "{\"MapId\":";
            const string reviewJsonMiddle = ",\"EvidenceFingerprint\":\"list-";
            const string reviewJsonSuffix = "\"}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [PurchaseReceiptAuditEvents]
                    ([StoreId],[StockDocumentId],[EventType],[ActorUserId],[OccurredAtUtc],[IsSuccess],[ChangedFieldsJson],[OldValuesJson],[NewValuesJson])
                SELECT {store.StoreId}, [m].[StockDocumentId], {reviewType}, {managerAccount.UserId}, {historicalTime}, 1, N'[]', {emptyJson},
                    CONCAT({reviewJsonPrefix}, [m].[Id], {reviewJsonMiddle}, [m].[StockDocumentId], {reviewJsonSuffix})
                FROM [StockDocumentInputInvoiceMap] AS [m]
                WHERE [m].[StoreId] = {store.StoreId} AND [m].[InputInvoiceHeadId] = {currentHead.Id} AND [m].[IsDeleted] = 0
                """);
            Assert.Equal(receiptCount * 10, await db.PurchaseReceiptAuditEvents.CountAsync());
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
            output.WriteLine($"SQL: {items.Count} summaries / 20,000 lines / 10,000 audit events / 2,000 invoice links in {timer.ElapsedMilliseconds} ms, {capture.Commands.Count} reads.");
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
            Assert.Equal("NeedsReview", special.InvoiceFollowUp); // Pending review must not become commercial confirmation.
            Assert.False(string.IsNullOrWhiteSpace(special.SupplierName));
            Assert.False(string.IsNullOrWhiteSpace(special.LegalEntityName));
            Assert.All(items.Where(x => x.Id != emptyId && x.Id != specialId), item =>
            {
                Assert.Equal(19, item.TotalLines); Assert.Equal(1, item.TotalProductTypes);
                Assert.True(item.HasLinkedInputInvoice);
                Assert.Equal(currentMapIds[item.Id], item.InvoiceMapId);
                Assert.Equal($"list-{item.Id}", item.InvoiceEvidenceFingerprint);
                Assert.Equal(InputInvoiceReconciliationState.Incomplete, item.InvoiceReconciliationState);
                Assert.Equal(item.Status == StockDocumentStatus.Confirmed ? "Reviewed" : "NeedsReview", item.InvoiceFollowUp);
            });
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(4, capture.Commands.Count); // Constant with 1,001 receipts; prevents an N+1 regression.
            Assert.Equal(0, capture.NonQueryCommands);
            Assert.DoesNotContain("ProductNameSnapshot", capture.Sql);
            Assert.DoesNotContain("UnitCost", capture.Sql);
            Assert.DoesNotContain("OldValuesJson", capture.Sql);
            Assert.DoesNotContain("ChangedFieldsJson", capture.Sql);
            Assert.Contains("COUNT", capture.Sql);
            Assert.Contains("MAX", capture.Sql);
        }
        using var manager = await app.LoginAsync(managerAccount);
        var receivingCapture = new ListReadCapture();
        await using (var db = app.Database.CreateTenantContext(store.StoreId, receivingCapture))
        {
            var timer = Stopwatch.StartNew();
            var active = await new StockDocumentRepository(db).GetReceivingReceiptListAsync();
            output.WriteLine($"Receiving SQL: {active.Count} active summaries in {timer.ElapsedMilliseconds} ms, {receivingCapture.Commands.Count} reads.");
            Assert.Equal(receiptCount / 2 + 1, active.Count);
            Assert.Equal(emptyId, active[0].Id);
            var special = Assert.Single(active, x => x.Id == specialId);
            Assert.Equal(19, special.TotalLines);
            Assert.Equal(2, special.TotalProductTypes);
            Assert.All(active.Skip(1), item => Assert.Equal(StockDocumentStatus.PendingApproval, item.Status));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(2, receivingCapture.Commands.Count);
            Assert.Equal(0, receivingCapture.NonQueryCommands);
            Assert.DoesNotContain("[StockDocumentInputInvoiceMap]", receivingCapture.Sql);
            Assert.DoesNotContain("[StockDocumentInputInvoiceReconciliations]", receivingCapture.Sql);
            Assert.DoesNotContain("AuditEvents", receivingCapture.Sql);
            Assert.DoesNotContain("Users", receivingCapture.Sql);
            Assert.DoesNotContain("ProductNameSnapshot", receivingCapture.Sql);
            Assert.All(receivingCapture.Commands, sql => Assert.Contains("[Status] IN", sql));
        }
        var receiving = await manager.JsonAsync(HttpMethod.Get, "/admin/warehouse-receiving/receipts");
        Assert.Equal(receiptCount / 2 + 1, receiving.GetArrayLength());
        Assert.All(receiving.EnumerateArray(), row => Assert.False(row.TryGetProperty("invoiceFollowUp", out _)));
        var endpointTimer = Stopwatch.StartNew();
        var json = await manager.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        Assert.Equal(receiptCount + 1, json.GetArrayLength());
        Assert.Equal(receiptCount / 2, json.EnumerateArray().Count(x => x.GetProperty("invoiceFollowUp").GetString() == "Reviewed"));
        Assert.All(json.EnumerateArray(), row =>
        {
            Assert.False(row.TryGetProperty("invoiceMapId", out _));
            Assert.False(row.TryGetProperty("invoiceEvidenceFingerprint", out _));
            Assert.False(row.TryGetProperty("invoiceReviewEvidenceJson", out _));
        });
        output.WriteLine($"HTTP: {json.GetArrayLength()} summaries in {endpointTimer.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Receipt_list_preserves_latest_successful_review_live_link_states_and_tenant_isolation()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        var otherStore = app.Stores[1];
        var otherAccount = await app.AddAccountAsync(otherStore, "*");
        var expected = new List<ListExpectation>();
        int reviewedId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
            var supplier = await db.Suppliers.SingleAsync();
            var head = NewInvoice(store.StoreId, supplier.Id, "8101");
            var oldHead = NewInvoice(store.StoreId, supplier.Id, "8102");
            db.AddRange(head, oldHead); await db.SaveChangesAsync();

            async Task<StockDocument> Document(string name, StockDocumentStatus status = StockDocumentStatus.Confirmed, bool? wait = true)
            {
                var document = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                    SupplierId = supplier.Id, DocumentNo = "MATRIX-" + name, DocumentDate = new(2026, 10, 9),
                    Status = status, ConfirmedLegalEntityId = status == StockDocumentStatus.Confirmed ? warehouse.LegalEntityId : null,
                    WaitForInputInvoice = wait, ApprovedAtUtc = status == StockDocumentStatus.Confirmed ? DateTime.UtcNow.Date.AddDays(-5) : null };
                db.Add(document); await db.SaveChangesAsync(); return document;
            }
            async Task<StockDocumentInputInvoiceMap> Link(StockDocument document, int invoiceId, InputInvoiceReconciliationState? state)
            {
                var map = new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = document.Id, InputInvoiceHeadId = invoiceId };
                db.Add(map); await db.SaveChangesAsync();
                if (state.HasValue)
                {
                    db.Add(NewReconciliation(store.StoreId, map, "matrix-evidence", state.Value)); await db.SaveChangesAsync();
                }
                return map;
            }
            var reviewNames = new[] { "Reviewed", "LinkAfterReview", "UnlinkAfterReview", "RelinkAfterReview", "FailedRelinkAfterReview",
                "FailedReviewAfterReview", "UnrelatedAfterReview", "MalformedReview", "WrongMapReview", "StaleFingerprintReview", "PendingReview" };
            reviewedId = 0;
            foreach (var name in reviewNames)
            {
                var status = name == "PendingReview" ? StockDocumentStatus.PendingApproval : StockDocumentStatus.Confirmed;
                var document = await Document(name, status);
                var map = await Link(document, head.Id, InputInvoiceReconciliationState.Incomplete);
                // IDs are authoritative even when an older imported event has a later timestamp.
                await InsertHistoricalAuditAsync(db, store.StoreId, document.Id, account.UserId,
                    PurchaseReceiptAuditEventType.InputInvoiceLinked, "{}", true, new(2035, 1, 1));
                string? review = name switch
                {
                    "MalformedReview" => "broken",
                    "WrongMapReview" => ReviewJson(map.Id + 100000, "matrix-evidence"),
                    "StaleFingerprintReview" => ReviewJson(map.Id, "stale-evidence"),
                    _ => ReviewJson(map.Id, "matrix-evidence")
                };
                await InsertHistoricalAuditAsync(db, store.StoreId, document.Id, account.UserId,
                    PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed, review, true, new(2001, 1, 1));
                var latestType = name switch
                {
                    "LinkAfterReview" => PurchaseReceiptAuditEventType.InputInvoiceLinked,
                    "UnlinkAfterReview" => PurchaseReceiptAuditEventType.InputInvoiceUnlinked,
                    "RelinkAfterReview" or "FailedRelinkAfterReview" => PurchaseReceiptAuditEventType.InputInvoiceRelinked,
                    "FailedReviewAfterReview" => PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed,
                    _ => (PurchaseReceiptAuditEventType?)null
                };
                if (latestType.HasValue)
                {
                    var success = name is not ("FailedRelinkAfterReview" or "FailedReviewAfterReview");
                    await InsertHistoricalAuditAsync(db, store.StoreId, document.Id, account.UserId,
                        latestType.Value, "{}", success, new(1999, 1, 1));
                    if (success) review = null;
                }
                if (name == "UnrelatedAfterReview")
                    await InsertHistoricalAuditAsync(db, store.StoreId, document.Id, account.UserId,
                        PurchaseReceiptAuditEventType.CommercialApprovalConfirmed, "{}", true, new(2036, 1, 1));
                var followUp = name is "Reviewed" or "FailedRelinkAfterReview" or "FailedReviewAfterReview" or "UnrelatedAfterReview" ? "Reviewed" : "NeedsReview";
                expected.Add(new(document.Id, followUp, true, map.Id, "matrix-evidence", InputInvoiceReconciliationState.Incomplete, review));
                if (name == "Reviewed") reviewedId = document.Id;
            }
            foreach (var state in new[] { InputInvoiceReconciliationState.Matched, InputInvoiceReconciliationState.AcceptedMismatch })
            {
                var document = await Document(state.ToString());
                var map = await Link(document, head.Id, state);
                expected.Add(new(document.Id, "Complete", true, map.Id, "matrix-evidence", state));
            }
            var deletedReconciliationDocument = await Document("DeletedReconciliation");
            var deletedReconciliationMap = await Link(deletedReconciliationDocument, head.Id, InputInvoiceReconciliationState.Matched);
            var deletedReconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync(x => x.StockDocumentId == deletedReconciliationDocument.Id);
            deletedReconciliation.IsDeleted = true; await db.SaveChangesAsync();
            expected.Add(new(deletedReconciliationDocument.Id, "NeedsReview", true, deletedReconciliationMap.Id, null, null));

            var relinkedDocument = await Document("OldMatchedMapNewUnreconciledMap");
            var oldMap = await Link(relinkedDocument, oldHead.Id, InputInvoiceReconciliationState.Matched);
            var oldReview = ReviewJson(oldMap.Id, "matrix-evidence");
            await InsertHistoricalAuditAsync(db, store.StoreId, relinkedDocument.Id, account.UserId,
                PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed, oldReview, true, new(2001, 1, 1));
            oldMap.IsDeleted = true; await db.SaveChangesAsync();
            var newMap = await Link(relinkedDocument, head.Id, null);
            expected.Add(new(relinkedDocument.Id, "NeedsReview", true, newMap.Id, null, null, oldReview));

            var deletedMapDocument = await Document("DeletedMapOnly");
            var deletedMap = await Link(deletedMapDocument, head.Id, InputInvoiceReconciliationState.Matched);
            deletedMap.IsDeleted = true; await db.SaveChangesAsync();
            expected.Add(new(deletedMapDocument.Id, "Waiting", false, null, null, null));
            var waiting = await Document("Waiting");
            expected.Add(new(waiting.Id, "Waiting", false, null, null, null));
            var notExpected = await Document("NotExpected", StockDocumentStatus.Rejected, false);
            expected.Add(new(notExpected.Id, "NotExpected", false, null, null, null));
            var unclassified = await Document("Unclassified", StockDocumentStatus.Draft, null);
            expected.Add(new(unclassified.Id, "Unclassified", false, null, null, null));
        }

        int foreignId;
        await using (var db = app.Database.CreateTenantContext(otherStore.StoreId))
        {
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == otherStore.WarehouseId);
            var supplier = await db.Suppliers.SingleAsync();
            var foreignReceipt = new StockDocument { StoreId = otherStore.StoreId, WarehouseId = otherStore.WarehouseId,
                DocumentNo = "MATRIX-Reviewed", Status = StockDocumentStatus.Confirmed, ConfirmedLegalEntityId = warehouse.LegalEntityId };
            var head = NewInvoice(otherStore.StoreId, supplier.Id, "8103");
            db.AddRange(foreignReceipt, head); await db.SaveChangesAsync(); foreignId = foreignReceipt.Id;
            var map = new StockDocumentInputInvoiceMap { StoreId = otherStore.StoreId, StockDocumentId = foreignId, InputInvoiceHeadId = head.Id };
            db.Add(map); await db.SaveChangesAsync();
            db.Add(NewReconciliation(otherStore.StoreId, map, "foreign-evidence", InputInvoiceReconciliationState.Matched)); await db.SaveChangesAsync();
            // Legacy evidence can have valid individual FKs yet inconsistent tenant attribution.
            // The list reader must never let this newer foreign-store event invalidate a local review.
            await InsertHistoricalAuditAsync(db, otherStore.StoreId, reviewedId, otherAccount.UserId,
                PurchaseReceiptAuditEventType.InputInvoiceRelinked, "{}", true, new(2037, 1, 1));
        }

        var capture = new ListReadCapture();
        await using (var db = app.Database.CreateTenantContext(store.StoreId, capture))
        {
            var items = await new StockDocumentRepository(db).GetReceiptListAsync();
            Assert.Equal(expected.Count, items.Count);
            Assert.DoesNotContain(items, x => x.Id == foreignId);
            Assert.Equal(items.Select(x => x.Id).OrderDescending(), items.Select(x => x.Id));
            foreach (var test in expected)
            {
                var item = Assert.Single(items, x => x.Id == test.Id);
                Assert.Equal(test.FollowUp, item.InvoiceFollowUp);
                Assert.Equal(test.Linked, item.HasLinkedInputInvoice);
                Assert.Equal(test.MapId, item.InvoiceMapId);
                Assert.Equal(test.Fingerprint, item.InvoiceEvidenceFingerprint);
                Assert.Equal(test.ReconciliationState, item.InvoiceReconciliationState);
                Assert.Equal(test.ReviewJson, item.InvoiceReviewEvidenceJson);
                Assert.Equal(0, item.TotalLines); Assert.Equal(0, item.TotalProductTypes);
                Assert.Equal(item.InvoiceFollowUp == "Waiting" ? Math.Max(0, (DateTime.UtcNow.Date - item.ApprovedAtUtc!.Value.Date).Days) : 0, item.InvoiceWaitingDays);
            }
            Assert.Equal(4, capture.Commands.Count); // Same number of reads as the 1,001-receipt workload.
            Assert.Equal(0, capture.NonQueryCommands);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var active = await new StockDocumentRepository(db).GetReceivingReceiptListAsync();
            Assert.Equal(new[] { StockDocumentStatus.Rejected, StockDocumentStatus.Draft, StockDocumentStatus.PendingApproval }, active.Select(x => x.Status));
            Assert.DoesNotContain(active, x => x.Id == foreignId);
        }
        var emptyCapture = new ListReadCapture();
        await using (var db = app.Database.CreateTenantContext(otherStore.StoreId, emptyCapture))
        {
            Assert.Empty(await new StockDocumentRepository(db).GetReceivingReceiptListAsync());
            Assert.Single(emptyCapture.Commands);
        }
        using var manager = await app.LoginAsync(account);
        var json = await manager.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        Assert.Equal(expected.Count, json.GetArrayLength());
        foreach (var test in expected)
        {
            var row = Assert.Single(json.EnumerateArray(), x => x.GetProperty("id").GetInt32() == test.Id);
            Assert.Equal(test.FollowUp, row.GetProperty("invoiceFollowUp").GetString());
            Assert.Equal(test.Linked, row.GetProperty("hasLinkedInputInvoice").GetBoolean());
            Assert.False(row.TryGetProperty("invoiceReviewEvidenceJson", out _));
        }
        using var other = await app.LoginAsync(otherAccount);
        var otherJson = await other.JsonAsync(HttpMethod.Get, "/admin/api/stock-documents/receipts");
        var foreignRow = Assert.Single(otherJson.EnumerateArray());
        Assert.Equal(foreignId, foreignRow.GetProperty("id").GetInt32());
        Assert.Equal("Complete", foreignRow.GetProperty("invoiceFollowUp").GetString());
    }

    private static InputInvoiceHead NewInvoice(int storeId, int supplierId, string number)
    {
        var head = new InputInvoiceHead { StoreId = storeId, SellerName = "List supplier", SellerTaxCode = "0312770607",
            BuyerTaxCode = "0101234567", InvoiceSeries = "C26LIST", InvoiceNumber = number, InvoiceDate = new(2026, 10, 9),
            ResolvedSupplierId = supplierId, SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head); return head;
    }

    private static StockDocumentInputInvoiceReconciliation NewReconciliation(int storeId, StockDocumentInputInvoiceMap map,
        string fingerprint, InputInvoiceReconciliationState state)
        => new() { StoreId = storeId, StockDocumentId = map.StockDocumentId, StockDocumentInputInvoiceMapId = map.Id,
            InputInvoiceHeadId = map.InputInvoiceHeadId, EvidenceFingerprint = fingerprint, OverallState = state, LastCalculatedAtUtc = DateTime.UtcNow };

    private static string ReviewJson(int mapId, string fingerprint)
        => JsonSerializer.Serialize(new { MapId = mapId, EvidenceFingerprint = fingerprint });

    private static Task<int> InsertHistoricalAuditAsync(AppDbContext db, int storeId, int documentId, int actorId,
        PurchaseReceiptAuditEventType type, string values, bool success, DateTime occurred)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [PurchaseReceiptAuditEvents]
                ([StoreId],[StockDocumentId],[EventType],[ActorUserId],[OccurredAtUtc],[IsSuccess],[ChangedFieldsJson],[OldValuesJson],[NewValuesJson])
            VALUES ({storeId},{documentId},{(int)type},{actorId},{occurred},{success},{"[]"},{"{}"},{values})
            """);

    private sealed record ListExpectation(int Id, string FollowUp, bool Linked, int? MapId, string? Fingerprint,
        InputInvoiceReconciliationState? ReconciliationState, string? ReviewJson = null);

    private sealed class ListReadCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public int NonQueryCommands { get; private set; }
        public string Sql => string.Join('\n', Commands);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            NonQueryCommands++;
            return ValueTask.FromResult(result);
        }
    }
}

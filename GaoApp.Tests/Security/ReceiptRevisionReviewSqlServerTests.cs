using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptRevisionReviewSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}";
    private static JsonElement Line(JsonElement document, int id) => document.GetProperty("lines")
        .EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);

    [Fact]
    public async Task Resubmitted_receipt_prioritizes_corrections_preserves_saved_prices_and_resets_for_each_return()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Approve));
        using var employee = await EmployeeAsync(app, store);
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));

        int packLineId, cartonLineId, packUnitId, cartonUnitId, transactionCount;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var lines = await db.StockDocumentLines.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync();
            var pack = lines.Single(x => x.ProductUnitConversionId == seed.PackId);
            var carton = lines.Single(x => x.ProductUnitConversionId == seed.CartonId);
            packLineId = pack.Id; packUnitId = pack.UnitId!.Value;
            cartonLineId = carton.Id; cartonUnitId = carton.UnitId!.Value;
            transactionCount = await db.InventoryTransactions.CountAsync();
        }

        var state = await GetAsync(manager, seed.ReceiptId);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("revisionReview").ValueKind);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", state, "Nhân viên gửi lần đầu");
        state = await GetAsync(manager, seed.ReceiptId);
        await SavePricesAsync(manager, seed.ReceiptId, state, (packLineId, 12345m), (cartonLineId, 23456m));
        state = await GetAsync(manager, seed.ReceiptId);

        // The existing workflow permissions also protect receipts carrying revision evidence.
        using (var response = await employee.Http.PostAsJsonAsync(Url(seed.ReceiptId) + "/reject", new
        { rowVersion = state.GetProperty("rowVersion").GetString(), note = "Không có quyền duyệt" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await outsider.Http.GetAsync(Url(seed.ReceiptId)))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await denied.Http.GetAsync(Url(seed.ReceiptId)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await WorkflowAsync(manager, seed.ReceiptId, "reject", state, "Thiếu hàng, sửa số lượng và nhập bù");
        await UpdatePhysicalAsync(employee, seed.ReceiptId, cartonLineId, cartonUnitId, 3m);
        var added = await employee.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + "/lines", new
        { productVariantId = store.VariantId, quantity = 5m, note = "Hàng nhập bù" });
        var addedLineId = added.GetProperty("lineId").GetInt32();
        state = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", state, "Đã sửa và bổ sung hàng thiếu");

        state = await GetAsync(manager, seed.ReceiptId);
        await AssertInitialRoundEvidenceAsync(app, store, seed.ReceiptId, addedLineId, state);
        AssertRound(state, added: 1, changed: 1, removed: 0);
        var review = state.GetProperty("revisionReview").GetRawText();
        Assert.Equal("changed", Line(state, cartonLineId).GetProperty("revisionChangeKind").GetString());
        Assert.Equal(2m, Line(state, cartonLineId).GetProperty("previousRevisionQuantity").GetDecimal());
        Assert.Equal(3m, Line(state, cartonLineId).GetProperty("quantity").GetDecimal());
        Assert.Contains(Line(state, cartonLineId).GetProperty("revisionChangedFields").EnumerateArray(),
            x => x.GetString() == nameof(StockDocumentLine.Quantity));
        Assert.NotEmpty(Line(state, cartonLineId).GetProperty("revisionChangeSummary").GetString()!);
        Assert.Equal("added", Line(state, addedLineId).GetProperty("revisionChangeKind").GetString());
        Assert.Equal(JsonValueKind.Null, Line(state, packLineId).GetProperty("revisionChangeKind").ValueKind);
        Assert.Equal(12345m, Line(state, packLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        Assert.Equal(23456m, Line(state, cartonLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        await AssertCommercialRowsAsync(manager, seed.ReceiptId,
            (cartonLineId, "changed"), (addedLineId, "added"), (packLineId, ""));

        // A new HTTP client sees the persisted correction round, and later manager price
        // work cannot expand that round or move an otherwise untouched line to the top.
        using (var freshManager = await app.LoginAsync(await app.AddAccountAsync(store,
                   PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Approve)))
        {
            var reloaded = await GetAsync(freshManager, seed.ReceiptId);
            Assert.Equal(review, reloaded.GetProperty("revisionReview").GetRawText());
            await SavePricesAsync(freshManager, seed.ReceiptId, reloaded, (packLineId, 13000m), (addedLineId, 15000m));
        }
        state = await GetAsync(manager, seed.ReceiptId);
        Assert.Equal(review, state.GetProperty("revisionReview").GetRawText());
        Assert.Equal(JsonValueKind.Null, Line(state, packLineId).GetProperty("revisionChangeKind").ValueKind);
        AssertRound(state, added: 1, changed: 1, removed: 0);

        // A second return compares with the latest review, not the first submission.
        var firstReturn = state.GetProperty("revisionReview").GetProperty("returnedAtUtc").GetDateTime();
        await WorkflowAsync(manager, seed.ReceiptId, "reject", state, "Sửa riêng dòng lốc; bỏ dòng nhập bù nhầm");
        await UpdatePhysicalAsync(employee, seed.ReceiptId, packLineId, packUnitId, 4m);
        var beforeDelete = await GetAsync(employee, seed.ReceiptId);
        await employee.JsonAsync(HttpMethod.Delete, Url(seed.ReceiptId) + $"/lines/{addedLineId}", new
        { rowVersion = beforeDelete.GetProperty("rowVersion").GetString(), lineRowVersion = Line(beforeDelete, addedLineId).GetProperty("rowVersion").GetString() });
        state = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", state, "Đã sửa vòng hai");
        state = await GetAsync(manager, seed.ReceiptId);
        AssertRound(state, added: 0, changed: 1, removed: 1);
        Assert.True(state.GetProperty("revisionReview").GetProperty("returnedAtUtc").GetDateTime() >= firstReturn);
        Assert.Equal("changed", Line(state, packLineId).GetProperty("revisionChangeKind").GetString());
        Assert.Equal(2m, Line(state, packLineId).GetProperty("previousRevisionQuantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, Line(state, cartonLineId).GetProperty("revisionChangeKind").ValueKind);
        Assert.Equal(13000m, Line(state, packLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        Assert.Equal(23456m, Line(state, cartonLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        await AssertCommercialRowsAsync(manager, seed.ReceiptId, (packLineId, "changed"), (cartonLineId, ""));

        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(StockDocumentStatus.PendingApproval,
            (await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        Assert.Equal(transactionCount, await check.InventoryTransactions.CountAsync());
        Assert.Equal(100m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.False(await check.PurchasePayables.AnyAsync());
    }

    [Fact]
    public async Task Returning_an_employee_revision_request_also_tracks_only_net_changes_in_that_round()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var employee = await EmployeeAsync(app, store);
        var state = await GetAsync(manager, seed.ReceiptId);
        var line = state.GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("lineNo").GetInt32()).First();
        var lineId = line.GetProperty("id").GetInt32();
        var unitId = line.GetProperty("unitId").GetInt32();
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", state, "Gửi duyệt");
        state = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "request-revision", state, "Xin sửa số lượng nhận nhầm");
        state = await GetAsync(manager, seed.ReceiptId);
        await manager.JsonAsync(HttpMethod.Post, Url(seed.ReceiptId) + "/resolve-revision-request", new
        { rowVersion = state.GetProperty("rowVersion").GetString(), returnToEdit = true, note = "Cho phép sửa hàng thực nhận" });
        await UpdatePhysicalAsync(employee, seed.ReceiptId, lineId, unitId, 6m);
        await UpdatePhysicalAsync(employee, seed.ReceiptId, lineId, unitId, 2m);
        state = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", state, "Đối chiếu lại: số lượng ban đầu đúng");
        state = await GetAsync(manager, seed.ReceiptId);
        AssertRound(state, added: 0, changed: 0, removed: 0);
        Assert.All(state.GetProperty("lines").EnumerateArray(),
            x => Assert.Equal(JsonValueKind.Null, x.GetProperty("revisionChangeKind").ValueKind));
    }

    [Fact]
    public async Task Employee_intake_known_scans_track_added_and_accumulated_rows_without_losing_saved_prices()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Approve));
        using var employee = await EmployeeAsync(app, store);
        int packLineId, cartonLineId, baseConversionId, transactionCount;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var lines = await db.StockDocumentLines.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync();
            packLineId = lines.Single(x => x.ProductUnitConversionId == seed.PackId).Id;
            cartonLineId = lines.Single(x => x.ProductUnitConversionId == seed.CartonId).Id;
            var baseUnitId = (await db.ProductVariants.Include(x => x.Product)
                .SingleAsync(x => x.Id == store.VariantId)).Product.BaseUnitId;
            var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId,
                UnitId = baseUnitId, IsBaseUnit = true, Factor = 1m, IsActive = true };
            db.ProductUnitConversions.Add(conversion);
            await db.SaveChangesAsync();
            baseConversionId = conversion.Id;
            db.ProductVariantUnitBarcodes.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId,
                ProductUnitConversionId = conversion.Id, Barcode = "REV-INTAKE-BASE", BarcodeType = BarcodeType.Internal,
                IsPrimary = true, IsActive = true });
            await db.SaveChangesAsync();
            transactionCount = await db.InventoryTransactions.CountAsync();
        }

        var document = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", document, "Gửi duyệt trước khi nhập bù");
        document = await GetAsync(manager, seed.ReceiptId);
        await SavePricesAsync(manager, seed.ReceiptId, document, (packLineId, 12345m), (cartonLineId, 23456m));
        document = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(manager, seed.ReceiptId, "reject", document, "Quét thêm thùng thiếu và bổ sung hàng đơn vị hộp");

        // This is the employee receiving screen's normal known-barcode route. Its
        // provisional repository must stage physical-line evidence before any save.
        var intakeUrl = Url(seed.ReceiptId) + "/intake";
        var intake = (await employee.JsonAsync(HttpMethod.Get, intakeUrl)).GetProperty("state");
        intake = await employee.JsonAsync(HttpMethod.Post, intakeUrl + "/known", new
        {
            commandId = Guid.NewGuid(), documentRowVersion = intake.GetProperty("documentRowVersion").GetString(),
            productUnitConversionId = seed.CartonId, factor = 24m, quantity = 1m, barcode = "INTERNAL-CARTON"
        });
        var baseScan = new
        {
            commandId = Guid.NewGuid(), documentRowVersion = intake.GetProperty("documentRowVersion").GetString(),
            productUnitConversionId = baseConversionId, factor = 1m, quantity = 5m, barcode = "REV-INTAKE-BASE"
        };
        await employee.JsonAsync(HttpMethod.Post, intakeUrl + "/known", baseScan);
        await employee.JsonAsync(HttpMethod.Post, intakeUrl + "/known", baseScan); // A retried scan must not double the added row.
        int addedLineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            addedLineId = (await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == seed.ReceiptId &&
                x.ProductUnitConversionId == baseConversionId)).Id;
        document = await GetAsync(manager, seed.ReceiptId);
        await WorkflowAsync(employee, seed.ReceiptId, "submit-approval", document, "Đã quét và nhận đủ hàng còn thiếu");

        document = await GetAsync(manager, seed.ReceiptId);
        await AssertInitialRoundEvidenceAsync(app, store, seed.ReceiptId, addedLineId, document);
        AssertRound(document, added: 1, changed: 1, removed: 0);
        Assert.Equal("added", Line(document, addedLineId).GetProperty("revisionChangeKind").GetString());
        Assert.Equal(5m, Line(document, addedLineId).GetProperty("quantity").GetDecimal());
        Assert.Equal("changed", Line(document, cartonLineId).GetProperty("revisionChangeKind").GetString());
        Assert.Equal(2m, Line(document, cartonLineId).GetProperty("previousRevisionQuantity").GetDecimal());
        Assert.Equal(3m, Line(document, cartonLineId).GetProperty("quantity").GetDecimal());
        Assert.Equal(23456m, Line(document, cartonLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        Assert.Equal(12345m, Line(document, packLineId).GetProperty("unitPriceBeforeVat").GetDecimal());
        Assert.Equal(JsonValueKind.Null, Line(document, packLineId).GetProperty("revisionChangeKind").ValueKind);
        await AssertCommercialRowsAsync(manager, seed.ReceiptId,
            (cartonLineId, "changed"), (addedLineId, "added"), (packLineId, ""));
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var returnedId = await check.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == seed.ReceiptId &&
            x.EventType == PurchaseReceiptAuditEventType.ReceiptRejected).MaxAsync(x => x.Id);
        var physicalEvidence = await check.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == seed.ReceiptId &&
            x.Id > returnedId && x.IsSuccess && (x.EventType == PurchaseReceiptAuditEventType.PhysicalLineAdded ||
                x.EventType == PurchaseReceiptAuditEventType.PhysicalLineChanged)).ToListAsync();
        Assert.Single(physicalEvidence, x => x.EventType == PurchaseReceiptAuditEventType.PhysicalLineAdded &&
            x.StockDocumentLineId == addedLineId);
        Assert.Contains(physicalEvidence, x => x.EventType == PurchaseReceiptAuditEventType.PhysicalLineChanged &&
            x.StockDocumentLineId == cartonLineId);
        Assert.Equal(transactionCount, await check.InventoryTransactions.CountAsync());
        Assert.Equal(100m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.False(await check.PurchasePayables.AnyAsync());
    }

    private static async Task<FullApplicationFixture.Client> EmployeeAsync(
        FullApplicationFixture app, FullApplicationFixture.StoreSeed store) => await app.LoginAsync(await app.AddAccountAsync(store,
        PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Create,
        PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Delete));

    private static Task<JsonElement> GetAsync(FullApplicationFixture.Client client, int id)
        => client.JsonAsync(HttpMethod.Get, Url(id));

    private static async Task UpdatePhysicalAsync(FullApplicationFixture.Client client, int documentId, int lineId, int unitId, decimal quantity)
    {
        var current = await GetAsync(client, documentId);
        await client.JsonAsync(HttpMethod.Put, Url(documentId) + $"/lines/{lineId}", new
        {
            unitId, quantity, rowVersion = current.GetProperty("rowVersion").GetString(),
            lineRowVersion = Line(current, lineId).GetProperty("rowVersion").GetString()
        });
    }

    private static Task<JsonElement> WorkflowAsync(FullApplicationFixture.Client client, int id,
        string action, JsonElement document, string note) => client.JsonAsync(HttpMethod.Post, Url(id) + "/" + action,
        new { rowVersion = document.GetProperty("rowVersion").GetString(), note });

    private static Task<JsonElement> SavePricesAsync(FullApplicationFixture.Client client, int id,
        JsonElement document, params (int Id, decimal Price)[] prices) => client.JsonAsync(HttpMethod.Post, Url(id) + "/price-draft", new
        {
            rowVersion = document.GetProperty("rowVersion").GetString(),
            lines = prices.Select(price => new { stockDocumentLineId = price.Id,
                rowVersion = Line(document, price.Id).GetProperty("rowVersion").GetString(), unitPriceBeforeVat = price.Price }).ToArray()
        });

    private static void AssertRound(JsonElement document, int added, int changed, int removed)
    {
        var review = document.GetProperty("revisionReview");
        Assert.Equal(added, review.GetProperty("addedLineCount").GetInt32());
        Assert.Equal(changed, review.GetProperty("changedLineCount").GetInt32());
        Assert.Equal(removed, review.GetProperty("removedLineCount").GetInt32());
        Assert.True(review.GetProperty("resubmittedAtUtc").GetDateTime() >= review.GetProperty("returnedAtUtc").GetDateTime());
    }

    private static async Task AssertInitialRoundEvidenceAsync(FullApplicationFixture app,
        FullApplicationFixture.StoreSeed store, int receiptId, int addedLineId, JsonElement document)
    {
        var review = document.GetProperty("revisionReview");
        if (review.ValueKind == JsonValueKind.Object &&
            review.GetProperty("addedLineCount").GetInt32() == 1 &&
            review.GetProperty("changedLineCount").GetInt32() == 1 &&
            review.GetProperty("removedLineCount").GetInt32() == 0) return;

        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var events = await db.PurchaseReceiptAuditEvents.AsNoTracking()
            .Where(x => x.StoreId == store.StoreId && x.StockDocumentId == receiptId)
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.EventType, x.StockDocumentLineId, x.IsSuccess,
                x.OldValuesJson, x.NewValuesJson, x.TraceId, x.OccurredAtUtc }).ToListAsync();
        var activeLines = document.GetProperty("lines").EnumerateArray()
            .Select(x => new { id = x.GetProperty("id").GetInt32(),
                quantity = x.GetProperty("quantity").GetDecimal(),
                changeKind = x.GetProperty("revisionChangeKind").GetString() }).ToArray();
        var diagnostic = JsonSerializer.Serialize(new
        {
            receiptId, addedLineId, revisionReview = review, activeLines,
            auditEvents = events.Select(x => new { x.Id, eventType = x.EventType.ToString(),
                lineId = x.StockDocumentLineId, x.IsSuccess, x.OldValuesJson, x.NewValuesJson, x.TraceId, x.OccurredAtUtc })
        }, new JsonSerializerOptions { WriteIndented = true });
        Assert.Fail("Unexpected first correction round in the disposable SQL fixture:\n" + diagnostic);
    }

    private static async Task AssertCommercialRowsAsync(FullApplicationFixture.Client client, int id,
        params (int Id, string Kind)[] expected)
    {
        var html = await client.Http.GetStringAsync($"/admin/stock-documents/{id}");
        var rows = Regex.Matches(html, "<tr\\b[^>]*class=\"commercial-line[^\"]*\"[^>]*>", RegexOptions.Singleline)
            .Select(match => match.Value).ToArray();
        Assert.Equal(expected.Length, rows.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Contains($"data-line-id=\"{expected[i].Id}\"", rows[i]);
            Assert.Contains($"data-revision-change=\"{expected[i].Kind}\"", rows[i]);
            Assert.Contains($"data-revision-changed=\"{(expected[i].Kind.Length > 0 ? "true" : "false")}\"", rows[i]);
            if (expected[i].Kind.Length > 0) Assert.Contains("sd-revision-line", rows[i]);
            else Assert.DoesNotContain("sd-revision-line", rows[i]);
        }
    }
}

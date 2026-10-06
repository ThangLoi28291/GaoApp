using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Claims;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Printing;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Printing;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class StoreMonitorHttpTests
{
    [Fact]
    public async Task B_uses_the_original_values_loaded_by_the_real_service_after_its_filter_read()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        int documentId, lineId, unitId;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var unit = await db.Units.SingleAsync(); unit.Name = "kg"; unitId = unit.Id;
            var document = new StockDocument { StoreId=seed.StoreId, WarehouseId=seed.WarehouseId,
                DocumentNo="MONITOR-BEFORE", DocumentDate=DateTime.Today, Type=GaoApp.Domain.Enums.StockDocumentType.Receipt };
            document.Lines.Add(new() { ProductVariantId=seed.ProductVariantId, ProductNameSnapshot="Saved rice",
                UnitId=unitId, UnitNameSnapshot="kg", Quantity=1, BaseQuantity=1, UnitCost=10, LineNo=1 });
            db.Add(document); await db.SaveChangesAsync(); documentId=document.Id; lineId=document.Lines.Single().Id;
        }
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "before-race");
        var registry = new StoreActivityRegistry(TimeProvider.System);
        var tickets = new StoreActivityTicket(new EphemeralDataProtectionProvider(), TimeProvider.System);
        AppDbContext ActorContext(int actor) => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString).Options, tenant, new MonitorActor(actor));
        async Task Run(AppDbContext db, int actor, UpdateStockDocumentLineRequest request, Func<Task>? beforeService = null)
        {
            var http = new DefaultHttpContext(); http.Request.Method="PUT";
            http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString()), new("full_name", $"Actor {actor}")], "test"));
            var descriptor = new ControllerActionDescriptor { ControllerName="StockDocuments", ActionName="UpdateLine" };
            var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor), [],
                new Dictionary<string,object?> { ["documentId"]=documentId, ["lineId"]=lineId, ["request"]=request }, new object());
            var repository = new StockDocumentRepository(db);
            // UpdateLine uses the real repository and unit resolver; unrelated workflows are not invoked.
            var service = new StockDocumentService(repository, null!, null!, null!, new InventoryUnitResolver(repository),
                null!, null!, null!, null!, tenant, null!, null!);
            var filter = new StoreActivityFilter(tenant, registry, tickets, db, NullLogger<StoreActivityFilter>.Instance);
            await filter.OnActionExecutionAsync(context, async () =>
            {
                if (beforeService is not null) await beforeService();
                await service.UpdateLineAsync(lineId, request);
                return new ActionExecutedContext(context, [], context.Controller) { Result=new OkObjectResult(new { success=true }) };
            });
        }
        var bReadPrior = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bDb = ActorContext(202);
        var bSaving = false;
        bDb.SavingChanges += (_, _) =>
        {
            var entry = bDb.ChangeTracker.Entries<StockDocumentLine>().Single(x => x.Entity.Id == lineId);
            Assert.Equal(2m, entry.OriginalValues.GetValue<decimal>(nameof(StockDocumentLine.Quantity)));
            Assert.Equal(20m, entry.OriginalValues.GetValue<decimal>(nameof(StockDocumentLine.UnitCost)));
            Assert.Equal(3m, entry.Entity.Quantity); Assert.Equal(20m, entry.Entity.UnitCost); bSaving=true;
        };
        var b = Run(bDb, 202, new() { UnitId=unitId, Quantity=3, UnitCost=null, Note="PRIVATE B NOTE" }, async () =>
        {
            // The filter has read 1/10, but the service has not yet loaded any tracked line.
            Assert.Empty(bDb.ChangeTracker.Entries<StockDocumentLine>());
            bReadPrior.SetResult(); await aCompleted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        });
        await bReadPrior.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var aDb = ActorContext(101);
            await Run(aDb, 101, new() { UnitId=unitId, Quantity=2, UnitCost=20, Note="PRIVATE A NOTE" });
        }
        finally { aCompleted.SetResult(); }
        await b; Assert.True(bSaving);
        var events = registry.Snapshot(seed.StoreId).Events; Assert.Equal(2, events.Count);
        var savedA = Assert.Single(events, x => x.UserId == 101); var savedB = Assert.Single(events, x => x.UserId == 202);
        Assert.Contains("1 kg → 2 kg", savedA.Detail); Assert.Contains("Giá nhập đã thay đổi", savedA.Detail);
        Assert.Contains("2 kg → 3 kg", savedB.Detail); Assert.DoesNotContain("Giá nhập đã thay đổi", savedB.Detail);
        Assert.All(events, x => { Assert.Equal($"receipt:{documentId}", x.WorkKey); Assert.Equal($"Phiếu nhập #{documentId}", x.Document);
            Assert.Equal("StockDocuments.UpdateLine", x.Action); Assert.DoesNotContain("PRIVATE", x.Detail);
            Assert.DoesNotContain("10", x.Detail); Assert.DoesNotContain("20", x.Detail); });
        await using var finalDb = database.CreateTenantContext(seed.StoreId);
        var final = await finalDb.StockDocumentLines.SingleAsync(x => x.Id == lineId);
        Assert.Equal(3m, final.Quantity); Assert.Equal(20m, final.UnitCost);
        await using var noSaveDb = database.CreateTenantContext(seed.StoreId);
        var noSaveHttp = new DefaultHttpContext(); noSaveHttp.Request.Method="PUT";
        noSaveHttp.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "303")], "test"));
        var noSaveDescriptor = new ControllerActionDescriptor { ControllerName="StockDocuments", ActionName="UpdateLine" };
        var noSaveContext = new ActionExecutingContext(new ActionContext(noSaveHttp, new RouteData(), noSaveDescriptor), [],
            new Dictionary<string,object?> { ["documentId"]=documentId, ["lineId"]=lineId,
                ["request"]=new { Quantity=999, UnitCost=888, Note="PRIVATE UNSAVED" } }, new object());
        var noSaveFilter = new StoreActivityFilter(tenant, registry, tickets, noSaveDb, NullLogger<StoreActivityFilter>.Instance);
        await noSaveFilter.OnActionExecutionAsync(noSaveContext, () => Task.FromResult(new ActionExecutedContext(noSaveContext, [], noSaveContext.Controller)
            { Result=new OkObjectResult(new { success=true }) }));
        var withoutCapture = Assert.Single(registry.Snapshot(seed.StoreId).Events, x => x.UserId == 303);
        Assert.DoesNotContain("→", withoutCapture.Detail); Assert.DoesNotContain("Giá nhập đã thay đổi", withoutCapture.Detail);
        Assert.DoesNotContain("999", withoutCapture.Detail); Assert.DoesNotContain("888", withoutCapture.Detail);
        Assert.DoesNotContain("PRIVATE", withoutCapture.Detail);
    }
    private sealed class MonitorActor(int actor) : ICurrentUser
    {
        public int? UserId => actor;
        public string? UserName => $"Actor {actor}";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
    [Fact]
    public async Task A_committed_line_is_immutable_when_B_commits_before_A_enrichment_and_rollback_publishes_nothing()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        int documentId, lineId;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var document = new StockDocument { StoreId=seed.StoreId, WarehouseId=seed.WarehouseId,
                DocumentNo="MONITOR-RACE", DocumentDate=DateTime.Today, Type=GaoApp.Domain.Enums.StockDocumentType.Receipt };
            document.Lines.Add(new() { ProductVariantId=seed.ProductVariantId, ProductNameSnapshot="Saved rice",
                UnitNameSnapshot="kg", Quantity=1, BaseQuantity=1, UnitCost=10, LineNo=1 });
            db.Add(document); await db.SaveChangesAsync(); documentId=document.Id; lineId=document.Lines.Single().Id;
        }
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "race");
        var registry = new StoreActivityRegistry(TimeProvider.System);
        var tickets = new StoreActivityTicket(new EphemeralDataProtectionProvider(), TimeProvider.System);
        ActionExecutingContext Context(int actor)
        {
            var http = new DefaultHttpContext(); http.Request.Method="PUT";
            http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString()), new("full_name", $"Actor {actor}")], "test"));
            var action = new ControllerActionDescriptor { ControllerName="StockDocuments", ActionName="UpdateLine" };
            return new(new ActionContext(http, new RouteData(), action), [], new Dictionary<string,object?> {
                ["documentId"]=documentId, ["lineId"]=lineId, ["request"]=new { Quantity=999, UnitCost=888, Note="PRIVATE NOTE", PhotoDataUrl="PRIVATE IMAGE" }
            }, new object());
        }
        async Task Run(AppDbContext db, int actor, Func<Task<IActionResult>> action)
        {
            var context = Context(actor);
            var filter = new StoreActivityFilter(tenant, registry, tickets, db, NullLogger<StoreActivityFilter>.Instance);
            await filter.OnActionExecutionAsync(context, async () => new ActionExecutedContext(context, [], context.Controller) { Result=await action() });
        }
        var aCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aDb = database.CreateTenantContext(seed.StoreId);
        var a = Run(aDb, 101, async () =>
        {
            await using var tx = await aDb.Database.BeginTransactionAsync();
            var line = await aDb.StockDocumentLines.Include(x => x.StockDocument).SingleAsync(x => x.Id == lineId);
            line.Quantity=line.BaseQuantity=2; await aDb.SaveChangesAsync();
            Assert.Empty(registry.Snapshot(seed.StoreId).Events);
            await tx.CommitAsync(); aCommitted.SetResult();
            await bPublished.Task.WaitAsync(TimeSpan.FromSeconds(20));
            return new OkObjectResult(new { success=true });
        });
        await aCommitted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var bDb = database.CreateTenantContext(seed.StoreId))
            await Run(bDb, 202, async () =>
            {
                var line = await bDb.StockDocumentLines.Include(x => x.StockDocument).SingleAsync(x => x.Id == lineId);
                Assert.Equal(2, line.Quantity); line.Quantity=line.BaseQuantity=3; await bDb.SaveChangesAsync();
                return new OkObjectResult(new { success=true });
            });
        var bEvent = Assert.Single(registry.Snapshot(seed.StoreId).Events); Assert.Equal(202, bEvent.UserId);
        bPublished.SetResult(); await a;
        var events = registry.Snapshot(seed.StoreId).Events;
        var savedA = Assert.Single(events, x => x.UserId == 101); var savedB = Assert.Single(events, x => x.UserId == 202);
        Assert.Contains("1 kg → 2 kg", savedA.Detail); Assert.Contains("2 kg → 3 kg", savedB.Detail);
        Assert.All(events, x => { Assert.Equal($"receipt:{documentId}", x.WorkKey); Assert.Equal($"Phiếu nhập #{documentId}", x.Document);
            Assert.Equal("StockDocuments.UpdateLine", x.Action); Assert.DoesNotContain("PRIVATE", x.Detail); Assert.DoesNotContain("999", x.Detail); Assert.DoesNotContain("888", x.Detail); });
        await using var rollbackDb = database.CreateTenantContext(seed.StoreId);
        await Run(rollbackDb, 303, async () =>
        {
            await using var tx = await rollbackDb.Database.BeginTransactionAsync();
            var line = await rollbackDb.StockDocumentLines.Include(x => x.StockDocument).SingleAsync(x => x.Id == lineId);
            line.Quantity=line.BaseQuantity=4; await rollbackDb.SaveChangesAsync(); await tx.RollbackAsync();
            return new ConflictObjectResult(new { success=false });
        });
        await Run(rollbackDb, 404, () => Task.FromResult<IActionResult>(new OkObjectResult(new { success=true, duplicate=true })));
        Assert.Equal(2, registry.Snapshot(seed.StoreId).Events.Count);
        Assert.Empty(registry.Snapshot(seed.StoreId+100000).Events);
        await using var finalDb = database.CreateTenantContext(seed.StoreId);
        Assert.Equal(3, (await finalDb.StockDocumentLines.SingleAsync(x => x.Id == lineId)).Quantity);
    }
    [Fact]
    public async Task Intake_capture_draft_quantity_and_removal_use_the_correct_saved_item()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        using var actor = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var monitor = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.StoreMonitorView));
        int unit, category;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.Products.SingleAsync(); unit = product.BaseUnitId; category = product.CategoryId;
        }
        var url = $"/admin/api/stock-documents/{seed.ReceiptId}/intake";
        async Task<JsonElement> Last() => (await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events")[0];
        DateTime oldDate; string receiptVersion; int owner, supplier;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            oldDate = document.DocumentDate; receiptVersion = Convert.ToBase64String(document.RowVersion);
            owner = await db.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync(); supplier = (await db.Suppliers.SingleAsync()).Id;
        }
        await actor.JsonAsync(HttpMethod.Post, "/admin/stock-documents/update-header", new { stockDocumentId=seed.ReceiptId, warehouseId=store.WarehouseId, legalEntityId=owner, supplierId=supplier, documentDate=oldDate.AddDays(-1), rowVersion=receiptVersion });
        Assert.Contains("Ngày phiếu:", (await Last()).GetProperty("detail").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) receiptVersion = Convert.ToBase64String((await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).RowVersion);
        using (var freight = await actor.Http.PostAsync($"/admin/stock-documents/{seed.ReceiptId}/freight", new FormUrlEncodedContent(new Dictionary<string,string> { ["RowVersion"]=receiptVersion, ["HasFreight"]="false" })))
        {
            Assert.Equal(HttpStatusCode.Redirect, freight.StatusCode);
            using var page = await actor.Http.GetAsync(freight.Headers.Location); page.EnsureSuccessStatusCode();
        }
        var freightEvent = await Last(); Assert.Equal("StockDocumentManagement.UpdateFreight", freightEvent.GetProperty("action").GetString()); Assert.Contains("Cước 0 ₫", freightEvent.GetProperty("detail").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) receiptVersion = Convert.ToBase64String((await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).RowVersion);
        using (var failedFreight = await actor.Http.PostAsync($"/admin/stock-documents/{seed.ReceiptId}/freight", new FormUrlEncodedContent(new Dictionary<string,string> { ["RowVersion"]=receiptVersion, ["HasFreight"]="true", ["FreightTotal"]="0" })))
        {
            Assert.Equal(HttpStatusCode.Redirect, failedFreight.StatusCode);
            using var page = await actor.Http.GetAsync(failedFreight.Headers.Location); page.EnsureSuccessStatusCode();
        }
        Assert.Equal(freightEvent.GetProperty("id").GetInt64(), (await Last()).GetProperty("id").GetInt64());
        var state = (await actor.JsonAsync(HttpMethod.Get, url)).GetProperty("state");
        state = await actor.JsonAsync(HttpMethod.Post, url, new CaptureReceiptIntakeRequest {
            CommandId=Guid.NewGuid(), DocumentRowVersion=state.GetProperty("documentRowVersion").GetString()!, Name="Đường vừa nhận", BaseUnitId=unit, UnitId=unit, Factor=1, Quantity=3, CategoryId=category, Note="PRIVATE INTAKE NOTE"
        });
        var item = Assert.Single(state.GetProperty("items").EnumerateArray(), x => x.GetProperty("name").GetString() == "Đường vừa nhận");
        var itemId = item.GetProperty("id").GetInt32();
        var captured = await Last(); Assert.Contains("Đường vừa nhận", captured.GetProperty("text").GetString()); Assert.Contains("Số lượng 3", captured.GetProperty("detail").GetString()); Assert.DoesNotContain("PRIVATE", captured.ToString());
        state = await actor.JsonAsync(HttpMethod.Post, url+$"/{itemId}/review", new ReviewReceiptIntakeRequest {
            CommandId=Guid.NewGuid(), DocumentRowVersion=state.GetProperty("documentRowVersion").GetString()!, ItemRowVersion=item.GetProperty("rowVersion").GetString()!, SaveDraftOnly=true,
            Completion=new() { Name="Đường đã kiểm tra", BaseUnitId=unit, UnitId=unit, Factor=1, Quantity=3, CategoryId=category }
        });
        var drafted = await Last(); Assert.Contains("lưu thông tin chờ duyệt", drafted.GetProperty("text").GetString()); Assert.DoesNotContain("vừa duyệt", drafted.GetProperty("text").GetString());
        item = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == itemId);
        state = await actor.JsonAsync(HttpMethod.Post, url+$"/{itemId}/quantity", new UpdateReceiptIntakeQuantityRequest {
            CommandId=Guid.NewGuid(), DocumentRowVersion=state.GetProperty("documentRowVersion").GetString()!, ItemRowVersion=item.GetProperty("rowVersion").GetString()!, Quantity=7
        });
        Assert.Contains("3", (await Last()).GetProperty("detail").GetString()); Assert.Contains("→ 7", (await Last()).GetProperty("detail").GetString());
        item = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == itemId);
        await actor.JsonAsync(HttpMethod.Post, url+$"/{itemId}/remove", new RemoveProvisionalItemRequest { CommandId=Guid.NewGuid(), DocumentRowVersion=state.GetProperty("documentRowVersion").GetString()!, ItemRowVersion=item.GetProperty("rowVersion").GetString()! });
        var removed = await Last(); Assert.Contains("Đường vừa nhận", removed.GetProperty("text").GetString()); Assert.Contains("Đã bỏ 7", removed.GetProperty("detail").GetString()); Assert.Equal($"receipt:{seed.ReceiptId}", removed.GetProperty("workKey").GetString());
        int priceLine; string priceLineVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
            document.Status = GaoApp.Domain.Enums.StockDocumentStatus.PendingApproval; await db.SaveChangesAsync();
            receiptVersion = Convert.ToBase64String(document.RowVersion); var row = document.Lines.OrderBy(x => x.LineNo).First(); priceLine=row.Id; priceLineVersion=Convert.ToBase64String(row.RowVersion);
        }
        await actor.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/price-draft", new { rowVersion=receiptVersion, lines=new[] { new { stockDocumentLineId=priceLine, rowVersion=priceLineVersion, unitPriceBeforeVat=12 } } });
        var price = await Last(); Assert.Contains("lưu giá nháp", price.GetProperty("text").GetString()); Assert.Contains("1 mặt hàng: Sữa tươi không đường", price.GetProperty("detail").GetString());
    }
    [Fact]
    public async Task Non_pos_events_identify_saved_goods_changes_deleted_rows_routes_and_actual_print_counts()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var actor = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var monitor = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.StoreMonitorView));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        int legalEntityId, unitId, destination;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            legalEntityId = await db.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var variant = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit).SingleAsync(x => x.Id == store.VariantId);
            variant.Product.Name = "Gạo ST25"; variant.Product.BaseUnit.Name = "kg"; unitId = variant.Product.BaseUnitId;
            var warehouse = new Warehouse { StoreId=store.StoreId, LegalEntityId=legalEntityId, Code="MONITOR-DEST", Name="Kho giao hàng" };
            db.Add(warehouse); await db.SaveChangesAsync(); destination = warehouse.Id;
        }
        async Task<JsonElement> Last() => (await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events")[0];
        var receipt = (await actor.JsonAsync(HttpMethod.Post, "/admin/warehouse-receiving/receipts", new { legalEntityId, warehouseId=store.WarehouseId, directReceiptReason="Khác" })).GetProperty("id").GetInt32();
        var line = (await actor.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{receipt}/lines", new { productVariantId=store.VariantId, quantity=5, note="PRIVATE NOTE", unitCost=999 })).GetProperty("lineId").GetInt32();
        var added = await Last(); Assert.Contains("Gạo ST25", added.GetProperty("text").GetString()); Assert.Contains("5 kg", added.GetProperty("detail").GetString());
        Assert.DoesNotContain("PRIVATE", added.ToString()); Assert.DoesNotContain("999", added.GetProperty("detail").GetString());
        await actor.JsonAsync(HttpMethod.Put, $"/admin/api/stock-documents/{receipt}/lines/{line}", new { quantity=8 });
        Assert.Contains("5 kg → 8 kg", (await Last()).GetProperty("detail").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var args = new Dictionary<string,object?> { ["documentId"]=receipt+100000, ["lineId"]=line };
            Assert.Null(await StoreActivityEnricher.Read(db, store.StoreId, "StockDocuments", "DeleteLine", args, null, null, default));
            args["documentId"] = receipt;
            Assert.Null(await StoreActivityEnricher.Read(db, app.Stores[1].StoreId, "StockDocuments", "DeleteLine", args, null, null, default));
        }
        var beforeDenied = (await Last()).GetProperty("id").GetInt64();
        using (var denied = await foreign.Http.DeleteAsync($"/admin/api/stock-documents/{receipt}/lines/{line}")) Assert.False(denied.IsSuccessStatusCode);
        using (var missing = await actor.Http.DeleteAsync($"/admin/api/stock-documents/{receipt+100000}/lines/{line}")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(beforeDenied, (await Last()).GetProperty("id").GetInt64());
        await actor.JsonAsync(HttpMethod.Delete, $"/admin/api/stock-documents/{receipt}/lines/{line}");
        var deleted = await Last(); Assert.Contains("Gạo ST25", deleted.GetProperty("text").GetString()); Assert.Contains("Đã bỏ 8 kg", deleted.GetProperty("detail").GetString()); Assert.Equal($"receipt:{receipt}", deleted.GetProperty("workKey").GetString());

        var count = (await actor.JsonAsync(HttpMethod.Post, "/admin/api/stock-counts", new { warehouseId=store.WarehouseId })).GetProperty("id").GetInt32();
        var countLine = (await actor.JsonAsync(HttpMethod.Post, $"/admin/api/stock-counts/{count}/lines", new { productVariantId=store.VariantId, unitId, countedQty=2 })).GetProperty("id").GetInt32();
        await actor.JsonAsync(HttpMethod.Put, $"/admin/api/stock-counts/lines/{countLine}", new { unitId, countedQty=0 });
        Assert.Contains("2 kg → 0 kg", (await Last()).GetProperty("detail").GetString());
        await actor.JsonAsync(HttpMethod.Post, $"/admin/api/stock-counts/{count}/submit-approval");
        var review = await Last(); Assert.Equal("review", review.GetProperty("module").GetString()); Assert.Contains("Gạo ST25", review.GetProperty("detail").GetString());

        var transfer = (await actor.JsonAsync(HttpMethod.Post, "/admin/api/stock-transfers", new { fromWarehouseId=store.WarehouseId, toWarehouseId=destination })).GetProperty("id").GetInt32();
        var transferLine = (await actor.JsonAsync(HttpMethod.Post, $"/admin/api/stock-transfers/{transfer}/lines", new { productVariantId=store.VariantId, unitId, quantity=3 })).GetProperty("id").GetInt32();
        var moved = await Last(); Assert.Contains("Gạo ST25", moved.GetProperty("text").GetString()); Assert.Contains("→ Kho giao hàng", moved.GetProperty("detail").GetString());
        await actor.JsonAsync(HttpMethod.Put, $"/admin/api/stock-transfers/lines/{transferLine}", new { unitId, quantity=6 });
        Assert.Contains("3 kg → 6 kg", (await Last()).GetProperty("detail").GetString());
        await actor.JsonAsync(HttpMethod.Delete, $"/admin/api/stock-transfers/lines/{transferLine}");
        Assert.Contains("Gạo ST25", (await Last()).GetProperty("text").GetString()); Assert.Contains("Đã bỏ 6 kg", (await Last()).GetProperty("detail").GetString());

        // Fake printer queue only. No physical print service is invoked by the Web fixture.
        var labelReceipt = await ProductLabelSqlServerTests.SeedReceipt(app, store);
        var printer = await actor.JsonAsync(HttpMethod.Post, "/admin/label-printing/printers", new SaveLabelPrinter { Name="TEST MONITOR", WindowsPrinterName="FAKE MONITOR" });
        var template = await actor.JsonAsync(HttpMethod.Post, "/admin/label-printing/templates", new SaveLabelTemplate(new() { PrinterId=printer.GetProperty("id").GetInt32(), QuantityMode="custom" }, null));
        var taskId = (await actor.JsonAsync(HttpMethod.Post, $"/admin/label-printing/receipts/{labelReceipt}")).GetProperty("id").GetInt32();
        var task = await actor.JsonAsync(HttpMethod.Get, $"/admin/label-printing/tasks/{taskId}");
        task = await actor.JsonAsync(HttpMethod.Put, $"/admin/label-printing/tasks/{taskId}/plan", new LabelPlanRequest(template.GetProperty("id").GetInt32(), [new(store.VariantId, 12)], task.GetProperty("rowVersion").GetString()!));
        var planned = await Last(); Assert.Contains("lập kế hoạch", planned.GetProperty("text").GetString()); Assert.Contains("Gạo ST25: 12 tem", planned.GetProperty("detail").GetString()); Assert.Equal($"label:{taskId}", planned.GetProperty("workKey").GetString());
        var jobRequest = new LabelJobRequest(taskId, template.GetProperty("id").GetInt32(), printer.GetProperty("id").GetInt32(), [new(store.VariantId, 12)], Guid.NewGuid(), task.GetProperty("rowVersion").GetString(), template.GetProperty("rowVersion").GetString()) { PlanLines=[new(store.VariantId, 12)] };
        var jobId = (await actor.JsonAsync(HttpMethod.Post, "/admin/label-printing/jobs", jobRequest)).GetProperty("id").GetInt32();
        var queued = await Last(); Assert.Contains("gửi in 12 tem", queued.GetProperty("text").GetString()); Assert.Contains("Gạo ST25", queued.GetProperty("detail").GetString());
        var queueEventId = queued.GetProperty("id").GetInt64();
        await actor.JsonAsync(HttpMethod.Post, "/admin/label-printing/jobs", jobRequest);
        Assert.Equal(queueEventId, (await Last()).GetProperty("id").GetInt64());
        string jobVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var job = await db.Set<ProductLabelJob>().SingleAsync(x => x.Id == jobId);
            job.Status = ProductLabelJobStatus.AwaitingConfirmation; await db.SaveChangesAsync(); jobVersion = Convert.ToBase64String(job.RowVersion);
            Assert.Null(await StoreActivityEnricher.Label(db, app.Stores[1].StoreId, "Confirm", new Dictionary<string,object?> { ["id"]=jobId }, null, default));
        }
        await actor.JsonAsync(HttpMethod.Post, $"/admin/label-printing/jobs/{jobId}/confirm", new LabelConfirmRequest(jobVersion, [new(store.VariantId, 8)], "PRIVATE PARTIAL PRINT REASON"));
        var confirmed = await Last(); Assert.Contains("8/12 tem", confirmed.GetProperty("text").GetString()); Assert.Contains("Gạo ST25: 8 tem", confirmed.GetProperty("detail").GetString()); Assert.Equal($"label:{taskId}", confirmed.GetProperty("workKey").GetString()); Assert.DoesNotContain("PRIVATE", confirmed.ToString());
        Assert.Empty((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events").EnumerateArray());
    }
    [Fact]
    public async Task Multiple_receipts_and_label_tasks_keep_signed_work_identity_per_employee_and_tab()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var firstAccount = await app.AddAccountAsync(store, "*"); var secondAccount = await app.AddAccountAsync(store, "*");
        using var first = await app.LoginAsync(firstAccount); using var second = await app.LoginAsync(secondAccount);
        using var monitor = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.StoreMonitorView));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        int legalEntityId, unitId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            legalEntityId = await db.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            unitId = await db.ProductVariants.Where(x => x.Id == store.VariantId).Select(x => x.Product.BaseUnitId).SingleAsync();
        }
        async Task<int> Receipt(FullApplicationFixture.Client actor) => (await actor.JsonAsync(HttpMethod.Post, "/admin/warehouse-receiving/receipts",
            new { legalEntityId, warehouseId=store.WarehouseId, directReceiptReason="Khác" })).GetProperty("id").GetInt32();
        var receipts = new[] { await Receipt(first), await Receipt(first), await Receipt(second) };
        async Task<string> Ticket(FullApplicationFixture.Client actor, string url) => WebUtility.HtmlDecode(
            Regex.Match(await actor.Http.GetStringAsync(url), "data-store-activity-ticket=\"([^\"]+)\"").Groups[1].Value);
        async Task Presence(FullApplicationFixture.Client actor, string ticket, Guid tab, string state="editing")
        {
            using var response = await actor.Http.PostAsync("/admin/api/store-activity/presence", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["ticket"]=ticket, ["tabId"]=tab.ToString(), ["state"]=state, ["workKey"]="receipt:999999", ["document"]="FORGED DOCUMENT" }));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        var firstTab = Guid.NewGuid(); var secondTab = Guid.NewGuid();
        var firstTicket = await Ticket(first, $"/admin/warehouse-receiving/{receipts[0]}");
        var secondTicket = await Ticket(first, $"/admin/warehouse-receiving/{receipts[1]}");
        await Presence(first, firstTicket, firstTab); await Presence(first, secondTicket, secondTab);
        await Presence(second, await Ticket(second, $"/admin/warehouse-receiving/{receipts[2]}"), Guid.NewGuid());
        var snapshot = await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
        var people = snapshot.GetProperty("people").EnumerateArray().ToArray();
        Assert.Equal(3, people.Length); Assert.Equal(2, people.Count(x => x.GetProperty("userId").GetInt32() == firstAccount.UserId));
        Assert.Equal(receipts.Order().Select(x => $"receipt:{x}"), people.Select(x => x.GetProperty("workKey").GetString()).Order());
        Assert.All(people, x => Assert.DoesNotContain("FORGED", x.GetProperty("document").GetString()));
        var events = snapshot.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(3, events.Length); Assert.Equal(3, events.Select(x => x.GetProperty("workKey").GetString()).Distinct().Count());
        await Presence(first, firstTicket, firstTab, "leave");
        Assert.Contains((await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("people").EnumerateArray(),
            x => x.GetProperty("workKey").GetString() == $"receipt:{receipts[1]}");
        using (var denied = await foreign.Http.GetAsync($"/admin/warehouse-receiving/{receipts[1]}")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var count = (await first.JsonAsync(HttpMethod.Post, "/admin/api/stock-counts", new { warehouseId=store.WarehouseId })).GetProperty("id").GetInt32();
        await Presence(first, await Ticket(first, $"/admin/stock-counts/{count}"), Guid.NewGuid());
        var added = await first.JsonAsync(HttpMethod.Post, $"/admin/api/stock-counts/{count}/lines", new { productVariantId=store.VariantId, unitId, countedQty=2 });
        await first.JsonAsync(HttpMethod.Put, $"/admin/api/stock-counts/lines/{added.GetProperty("id").GetInt32()}", new { unitId, countedQty=0 });
        var last = (await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events")[0];
        Assert.Equal($"count:{count}", last.GetProperty("workKey").GetString()); Assert.Equal($"Kiểm kê #{count}", last.GetProperty("document").GetString());
        Assert.Contains("2", last.GetProperty("detail").GetString()); Assert.Contains("→ 0", last.GetProperty("detail").GetString());
        Assert.Contains("Chênh lệch", last.GetProperty("detail").GetString());
        // The warehouse page is a shell; a cross-store ID must never become a signed work identity.
        await Presence(foreign, await Ticket(foreign, $"/admin/stock-counts/{count}"), Guid.NewGuid());
        var outside = Assert.Single((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("people").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, outside.GetProperty("workKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, outside.GetProperty("document").ValueKind);

        int[] labelIds;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var labels = receipts.Take(2).Select(id => new ProductLabelTask { StoreId=store.StoreId, StockDocumentId=id, DocumentNo=$"MONITOR-{id}" }).ToArray();
            db.AddRange(labels); await db.SaveChangesAsync(); labelIds = labels.Select(x => x.Id).ToArray();
        }
        var labelTab = Guid.NewGuid();
        foreach (var id in labelIds)
        {
            using var response = await first.Http.GetAsync($"/admin/label-printing/tasks/{id}"); response.EnsureSuccessStatusCode();
            var signed = Assert.Single(response.Headers.GetValues("X-Gao-Activity-Ticket"));
            await Presence(first, signed, labelTab);
            var labelPerson = Assert.Single((await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("people").EnumerateArray(),
                x => x.GetProperty("module").GetString() == "label");
            Assert.Equal($"label:{id}", labelPerson.GetProperty("workKey").GetString());
            Assert.Equal($"Phiếu tem #{id}", labelPerson.GetProperty("document").GetString());
        }
        using var foreignLabel = await foreign.Http.GetAsync($"/admin/label-printing/tasks/{labelIds[0]}");
        Assert.False(foreignLabel.IsSuccessStatusCode); Assert.False(foreignLabel.Headers.Contains("X-Gao-Activity-Ticket"));
    }
    [Fact]
    public async Task Real_mutations_reach_the_office_stream_and_presence_is_bound_to_the_authorized_employee()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var staffAccount = await app.AddAccountAsync(store, "*");
        var monitorAccount = await app.AddAccountAsync(store, PermissionCodes.Admin.StoreMonitorView);
        using var staff = await app.LoginAsync(staffAccount);
        using var monitor = await app.LoginAsync(monitorAccount);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], PermissionCodes.Admin.StoreMonitorView));
        using var noPermission = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView));
        using var denied = await noPermission.Http.GetAsync("/admin/api/store-activity/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var monitorWrite = await monitor.Http.PostAsync("/admin/pos/draft", null);
        Assert.Equal(HttpStatusCode.Forbidden, monitorWrite.StatusCode);
        Assert.Empty((await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events").EnumerateArray());

        var page = await staff.Http.GetStringAsync("/admin/warehouse-receiving");
        var ticket = WebUtility.HtmlDecode(Regex.Match(page, "data-store-activity-ticket=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(ticket);
        var tab = Guid.NewGuid();
        async Task<HttpResponseMessage> Presence(FullApplicationFixture.Client client, string value, string state) =>
            await client.Http.PostAsync("/admin/api/store-activity/presence", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["ticket"] = value, ["tabId"] = tab.ToString(), ["state"] = state, ["userId"] = monitorAccount.UserId.ToString(), ["module"] = "pos" }));
        using (var bad = await Presence(staff, "forged", "editing")) Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        using (var bad = await Presence(foreign, ticket, "editing")) Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        using (var valid = await Presence(staff, ticket, "editing")) Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        var current = await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
        var person = Assert.Single(current.GetProperty("people").EnumerateArray());
        Assert.Equal(staffAccount.UserId, person.GetProperty("userId").GetInt32());
        Assert.Equal("receipt", person.GetProperty("module").GetString());
        Assert.Equal("editing", person.GetProperty("state").GetString());
        Assert.Empty((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("people").EnumerateArray());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/store-activity/stream");
        using var streamResponse = await monitor.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await streamResponse.Content.ReadAsStreamAsync());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first = await ReadEvent(reader, deadline.Token); Assert.Equal("snapshot", first.Name);
        await staff.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        staff.Http.DefaultRequestHeaders.Add("X-POS-Operation-Id", Guid.NewGuid().ToString());
        var draft = await staff.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var replay = await staff.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        Assert.Equal(draft.GetProperty("orderId").GetInt32(), replay.GetProperty("orderId").GetInt32());
        staff.Http.DefaultRequestHeaders.Remove("X-POS-Operation-Id");
        var next = await ReadEvent(reader, deadline.Token);
        while (next.Name != "snapshot") next = await ReadEvent(reader, deadline.Token);
        var created = Assert.Single(next.Payload.GetProperty("events").EnumerateArray());
        Assert.Equal(staffAccount.UserId, created.GetProperty("userId").GetInt32());
        Assert.Equal("#" + draft.GetProperty("orderId").GetInt32(), created.GetProperty("document").GetString());
        // The inner POS operation filter rolls back a write when its offline total does not match.
        // Neither this attempted write nor an idempotent replay may appear as a new activity.
        using (var rollbackRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/admin/pos/{draft.GetProperty("orderId").GetInt32()}/items?variantId={store.VariantId}&qty=2"))
        {
            rollbackRequest.Headers.Add("X-POS-Operation-Id", Guid.NewGuid().ToString());
            rollbackRequest.Headers.Add("X-POS-Expected-Total", "999999");
            using var rolledBack = await staff.Http.SendAsync(rollbackRequest);
            Assert.Equal(HttpStatusCode.Conflict, rolledBack.StatusCode);
        }
        Assert.Single((await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events").EnumerateArray());
        await staff.JsonAsync(HttpMethod.Post, $"/admin/pos/{draft.GetProperty("orderId").GetInt32()}/items?variantId={store.VariantId}&qty=2");
        var detailed = (await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events")[0];
        Assert.Equal("POS.AddItem", detailed.GetProperty("action").GetString());
        Assert.Contains("Số lượng 2", detailed.GetProperty("detail").GetString());
        Assert.Contains("Tổng 40 ₫", detailed.GetProperty("detail").GetString());
        Assert.Equal("#" + draft.GetProperty("orderId").GetInt32(), detailed.GetProperty("document").GetString());
        Assert.Empty((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("events").EnumerateArray());
        using (var leave = await Presence(staff, ticket, "leave")) leave.EnsureSuccessStatusCode();

        // Permissions are checked again before each outgoing data frame and during idle heartbeats.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var grants = await db.RolePermissions.Where(x => x.RoleId == monitorAccount.RoleId).ToListAsync();
            db.RolePermissions.RemoveRange(grants); await db.SaveChangesAsync();
        }
        var revoked = await ReadEvent(reader, deadline.Token);
        while (revoked.Name != "revoked") revoked = await ReadEvent(reader, deadline.Token);
        Assert.Equal("revoked", revoked.Name);
        using var revokedSnapshot = await monitor.Http.GetAsync("/admin/api/store-activity/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, revokedSnapshot.StatusCode);
    }
    private static async Task<(string Name, JsonElement Payload)> ReadEvent(StreamReader reader, CancellationToken ct)
    {
        string? name = null, data = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: ")) name = line[7..];
            if (line.StartsWith("data: ")) data = line[6..];
            if (line.Length == 0 && name is not null && data is not null)
                return (name, JsonDocument.Parse(data).RootElement.Clone());
        }
        throw new InvalidOperationException("Store monitor stream ended before the expected event.");
    }
}

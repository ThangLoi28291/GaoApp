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
using GaoApp.Application.Services.Purchases;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Printing;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Infrastructure.Services.Products;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Printing;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
    private static AppDbContext ActorDb(InventoryPostingLocalDb database, TenantContext tenant, int actor, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).AddInterceptors(interceptors).Options, tenant, new MonitorActor(actor));
    private static StockDocumentService ReceiptService(AppDbContext db, TenantContext tenant, int actor)
    {
        var repository = new StockDocumentRepository(db);
        return new(repository, null!, new WarehouseRepository(db), null!, new InventoryUnitResolver(repository),
            null!, null!, null!, null!, tenant, null!, new MonitorActor(actor));
    }
    private static async Task MonitorMutation(AppDbContext db, TenantContext tenant, StoreActivityRegistry registry, int actor,
        string controller, string action, Dictionary<string,object?> args, Func<Task<IActionResult>> mutate, Controller? page = null)
    {
        var http = new DefaultHttpContext(); http.Request.Method="POST";
        http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString()), new("full_name", $"Actor {actor}")], "test"));
        var descriptor = new ControllerActionDescriptor { ControllerName=controller, ActionName=action };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor), [], args, (object?)page ?? new object());
        var filter = new StoreActivityFilter(tenant, registry, new StoreActivityTicket(new EphemeralDataProtectionProvider(), TimeProvider.System), db, NullLogger<StoreActivityFilter>.Instance);
        await filter.OnActionExecutionAsync(context, async () => new ActionExecutedContext(context, [], context.Controller) { Result=await mutate() });
    }
    private sealed class MonitorPage : Controller { }
    private sealed class MonitorTempData : ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context) => new Dictionary<string,object>();
        public void SaveTempData(HttpContext context, IDictionary<string,object> values) { }
    }
    private sealed class AfterCommitBarrier(Func<Task> barrier) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => barrier();
    }
    private static StockDocumentProvisionalItemService IntakeService(AppDbContext db, TenantContext tenant, int actor) =>
        new(new StockDocumentProvisionalItemRepository(db), new AppUnitOfWork(db), null!, null!, null!, tenant, new MonitorActor(actor),
            new ReceiptIntakeCatalog(db,null!,null!,null!,new MonitorActor(actor),null!,null!,null!));
    private static ProvisionalReceivingMutationRequest CopyIntent(ProvisionalReceivingMutationRequest request) =>
        (ProvisionalReceivingMutationRequest)JsonSerializer.Deserialize(JsonSerializer.Serialize(request,request.GetType()),request.GetType())!;
    private static Controller FreightPage()
    {
        var http = new DefaultHttpContext();
        return new MonitorPage { TempData=new TempDataDictionary(http, new MonitorTempData()) };
    }
    [Fact]
    public async Task Real_header_and_freight_saves_keep_each_actors_facts_across_later_commits()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(); int documentId, destination, owner;
        var day = new DateTime(2026, 10, 1);
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var source = await db.Warehouses.SingleAsync(); source.Name="Warehouse one"; owner=source.LegalEntityId;
            var second = new Warehouse { StoreId=seed.StoreId, LegalEntityId=owner, Code="MONITOR-W2", Name="Warehouse two", IsActive=true };
            var receipt = new StockDocument { StoreId=seed.StoreId, WarehouseId=source.Id, DocumentNo="MONITOR-HEADER", DocumentDate=day, Type=StockDocumentType.Receipt };
            db.AddRange(second, receipt); await db.SaveChangesAsync(); destination=second.Id; documentId=receipt.Id;
        }
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "headers"); var registry = new StoreActivityRegistry(TimeProvider.System);
        var bEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bDb = ActorDb(database, tenant, 202);
        var b = MonitorMutation(bDb, tenant, registry, 202, "StockDocumentManagement", "UpdateHeader",
            new() { ["request"]=new UpdateStockDocumentHeaderRequest { StockDocumentId=documentId, WarehouseId=destination, LegalEntityId=owner, Note="PRIVATE B NOTE" } }, async () =>
            {
                Assert.Empty(bDb.ChangeTracker.Entries<StockDocument>());
                // A legacy pre-filter reader sees W1/D1. The service will load W2/D2 after A.
                var prior = await bDb.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
                Assert.Equal(seed.WarehouseId, prior.WarehouseId); Assert.Equal(day, prior.DocumentDate);
                bEntered.SetResult(); await aSaved.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await ReceiptService(bDb, tenant, 202).UpdateHeaderAsync(new() { StockDocumentId=documentId, WarehouseId=destination, LegalEntityId=owner, Note="PRIVATE B NOTE" });
                await using (var laterDb = ActorDb(database, tenant, 606))
                    await ReceiptService(laterDb, tenant, 606).UpdateHeaderAsync(new() { StockDocumentId=documentId, WarehouseId=seed.WarehouseId, LegalEntityId=owner, DocumentDate=day.AddDays(2), Note="PRIVATE LATER" });
                bSaved.SetResult(); return new OkObjectResult(new { success=true });
            });
        await bEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using var aDb = ActorDb(database, tenant, 101);
        var a = MonitorMutation(aDb, tenant, registry, 101, "StockDocumentManagement", "UpdateHeader",
            new() { ["request"]=new UpdateStockDocumentHeaderRequest { StockDocumentId=documentId, WarehouseId=destination, LegalEntityId=owner, DocumentDate=day.AddDays(1) } }, async () =>
            {
                await ReceiptService(aDb, tenant, 101).UpdateHeaderAsync(new() { StockDocumentId=documentId, WarehouseId=destination, LegalEntityId=owner, DocumentDate=day.AddDays(1), Note="PRIVATE A NOTE" });
                aSaved.SetResult(); await bSaved.Task.WaitAsync(TimeSpan.FromSeconds(20)); return new OkObjectResult(new { success=true });
            });
        await Task.WhenAll(a,b);
        var eventA = Assert.Single(registry.Snapshot(seed.StoreId).Events, x => x.UserId == 101);
        var eventB = Assert.Single(registry.Snapshot(seed.StoreId).Events, x => x.UserId == 202);
        Assert.Contains("Warehouse one → Warehouse two", eventA.Detail); Assert.Contains("01/10/2026 → 02/10/2026", eventA.Detail);
        Assert.DoesNotContain("Đổi kho", eventB.Detail); Assert.DoesNotContain("Ngày phiếu", eventB.Detail);
        var freightSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterFreight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SaveFreight(int actor, decimal amount, Func<Task>? hold = null)
        {
            await using var db = ActorDb(database, tenant, actor); var page=FreightPage();
            var version=Convert.ToBase64String((await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId)).RowVersion);
            var request=new UpdatePurchaseReceiptApprovalRequest { RowVersion=version, HasFreight=true, FreightTotal=amount, FreightPayeeName="PRIVATE PAYEE", FreightNote="PRIVATE FREIGHT" };
            await MonitorMutation(db, tenant, registry, actor, "StockDocumentManagement", "UpdateFreight", new() { ["id"]=documentId, ["request"]=request }, async () =>
            {
                await ReceiptService(db, tenant, actor).UpdatePurchaseReceiptApprovalAsync(documentId, request);
                if (hold is not null) await hold(); page.TempData["Success"]="saved"; return new RedirectToActionResult("Edit", null, null);
            }, page);
        }
        var firstFreight = SaveFreight(303,10, async () => { freightSaved.SetResult(); await laterFreight.Task.WaitAsync(TimeSpan.FromSeconds(20)); });
        await freightSaved.Task.WaitAsync(TimeSpan.FromSeconds(20)); await SaveFreight(404,20); laterFreight.SetResult(); await firstFreight;
        Assert.Contains("Cước 10 ₫", Assert.Single(registry.Snapshot(seed.StoreId).Events, x => x.UserId == 303).Detail);
        Assert.Contains("Cước 20 ₫", Assert.Single(registry.Snapshot(seed.StoreId).Events, x => x.UserId == 404).Detail);
        await using (var db = ActorDb(database, tenant, 505))
        {
            var page=FreightPage(); var request=new UpdatePurchaseReceiptApprovalRequest { RowVersion=Convert.ToBase64String((await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId)).RowVersion), HasFreight=true, FreightTotal=0 };
            await MonitorMutation(db, tenant, registry, 505, "StockDocumentManagement", "UpdateFreight", new() { ["id"]=documentId, ["request"]=request }, async () =>
            {
                await Assert.ThrowsAsync<BusinessRuleException>(() => ReceiptService(db, tenant, 505).UpdatePurchaseReceiptApprovalAsync(documentId,request));
                page.TempData["Error"]="rejected"; return new RedirectToActionResult("Edit", null, null);
            }, page);
        }
        Assert.Equal(4, registry.Snapshot(seed.StoreId).Events.Count);
        Assert.All(registry.Snapshot(seed.StoreId).Events, x => { Assert.Equal($"receipt:{documentId}", x.WorkKey); Assert.DoesNotContain("PRIVATE", x.Detail); });
    }
    [Theory]
    [InlineData("StockDocuments", "receipt")]
    [InlineData("StockCounts", "count")]
    [InlineData("StockTransfers", "transfer")]
    public async Task Real_delete_reports_the_successful_soft_delete_tombstone_and_failed_delete_has_no_event(string controller, string kind)
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(); int parentId, lineId, unitId;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var unit=await db.Units.SingleAsync(); unit.Name="kg"; unitId=unit.Id;
            if (controller == "StockDocuments")
            {
                var parent=new StockDocument { StoreId=seed.StoreId, WarehouseId=seed.WarehouseId, DocumentNo="MONITOR-DELETE", DocumentDate=DateTime.Today, Type=StockDocumentType.Receipt };
                parent.Lines.Add(new() { ProductVariantId=seed.ProductVariantId, ProductNameSnapshot="Actual goods", UnitId=unitId, UnitNameSnapshot="kg", Quantity=1, BaseQuantity=1, LineNo=1 });
                db.Add(parent); await db.SaveChangesAsync(); parentId=parent.Id; lineId=parent.Lines.Single().Id;
            }
            else if (controller == "StockCounts")
            {
                var parent=new StockCountDocument { StoreId=seed.StoreId, WarehouseId=seed.WarehouseId, DocumentNo="MONITOR-DELETE", DocumentDate=DateTime.Today };
                parent.Lines.Add(new() { StoreId=seed.StoreId, ProductVariantId=seed.ProductVariantId, UnitId=unitId, ProductNameSnapshot="Actual goods", UnitNameSnapshot="kg", CountedQty=1, CountedQtyBase=1, Factor=1, LineNo=1 });
                db.Add(parent); await db.SaveChangesAsync(); parentId=parent.Id; lineId=parent.Lines.Single().Id;
            }
            else
            {
                var source=await db.Warehouses.SingleAsync();
                var destination=new Warehouse { StoreId=seed.StoreId, LegalEntityId=source.LegalEntityId, Code="MONITOR-DELETE-DEST", Name="Destination", IsActive=true };
                db.Add(destination); await db.SaveChangesAsync();
                var parent=new StockTransferDocument { StoreId=seed.StoreId, FromWarehouseId=source.Id, ToWarehouseId=destination.Id, DocumentNo="MONITOR-DELETE", DocumentDate=DateTime.Today };
                parent.Lines.Add(new() { StoreId=seed.StoreId, ProductVariantId=seed.ProductVariantId, UnitId=unitId, ProductNameSnapshot="Actual goods", UnitNameSnapshot="kg", Quantity=1, BaseQuantity=1, Factor=1, LineNo=1 });
                db.Add(parent); await db.SaveChangesAsync(); parentId=parent.Id; lineId=parent.Lines.Single().Id;
            }
        }
        var tenant=new TenantContext(); tenant.SetStore(seed.StoreId,"delete"); var registry=new StoreActivityRegistry(TimeProvider.System);
        async Task SetQuantity(AppDbContext db, decimal quantity)
        {
            if (controller == "StockDocuments") { var row=await db.StockDocumentLines.SingleAsync(x => x.Id == lineId); row.Quantity=row.BaseQuantity=quantity; }
            else if (controller == "StockCounts") { var row=await db.StockCountLines.SingleAsync(x => x.Id == lineId); row.CountedQty=row.CountedQtyBase=quantity; }
            else { var row=await db.StockTransferLines.SingleAsync(x => x.Id == lineId); row.Quantity=row.BaseQuantity=quantity; }
            await db.SaveChangesAsync();
        }
        Task Delete(AppDbContext db) => controller switch {
            "StockDocuments" => ReceiptService(db,tenant,101).DeleteLineAsync(lineId),
            "StockCounts" => new StockCountService(new StockCountRepository(db),null!,null!,null!).DeleteLineAsync(lineId),
            _ => new StockTransferService(new StockTransferRepository(db),null!,null!,null!).DeleteLineAsync(lineId) };
        var args=new Dictionary<string,object?> { ["documentId"]=parentId, ["lineId"]=lineId };
        await using (var deleteDb=ActorDb(database,tenant,101))
        {
            var prior=await StoreActivityEnricher.Read(deleteDb,seed.StoreId,controller,"UpdateLine",args,null,null,default);
            Assert.Equal(1m,prior!.Line?.Quantity);
            await using var staleDb=ActorDb(database,tenant,303);
            if (controller == "StockDocuments") await staleDb.StockDocumentLines.Include(x => x.StockDocument).SingleAsync(x => x.Id == lineId);
            else if (controller == "StockCounts") await staleDb.StockCountLines.Include(x => x.StockCountDocument).SingleAsync(x => x.Id == lineId);
            else await staleDb.StockTransferLines.Include(x => x.StockTransferDocument).SingleAsync(x => x.Id == lineId);
            await using (var writer=ActorDb(database,tenant,202)) await SetQuantity(writer,2);
            await MonitorMutation(staleDb,tenant,registry,303,controller,"DeleteLine",args,async () =>
            {
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Delete(staleDb));
                return new ConflictObjectResult(new { success=false });
            });
            Assert.Empty(registry.Snapshot(seed.StoreId).Events);
            await MonitorMutation(deleteDb,tenant,registry,101,controller,"DeleteLine",args,async () => { await Delete(deleteDb); return new OkObjectResult(new { success=true }); });
        }
        var activity=Assert.Single(registry.Snapshot(seed.StoreId).Events);
        Assert.Equal(101,activity.UserId); Assert.Equal($"{kind}:{parentId}",activity.WorkKey);
        Assert.Contains("Actual goods",activity.Text); Assert.Contains("Đã bỏ 2 kg",activity.Detail); Assert.DoesNotContain("1 kg",activity.Detail);
        await using var inspect=database.CreateTenantContext(seed.StoreId);
        if (controller == "StockDocuments") { var row=await inspect.StockDocumentLines.IgnoreQueryFilters().SingleAsync(x => x.Id == lineId); Assert.True(row.IsDeleted); Assert.Equal(2m,row.Quantity); }
        else if (controller == "StockCounts") { var row=await inspect.StockCountLines.IgnoreQueryFilters().SingleAsync(x => x.Id == lineId); Assert.True(row.IsDeleted); Assert.Equal(2m,row.CountedQty); }
        else { var row=await inspect.StockTransferLines.IgnoreQueryFilters().SingleAsync(x => x.Id == lineId); Assert.True(row.IsDeleted); Assert.Equal(2m,row.Quantity); }
        // Failed business result and uncommitted capture cannot add a deletion animation.
        await MonitorMutation(inspect,tenant,registry,303,controller,"DeleteLine",args,() => Task.FromResult<IActionResult>(new ConflictObjectResult(new { success=false })));
        Assert.Single(registry.Snapshot(seed.StoreId).Events);
    }
    [Theory]
    [InlineData("Quantity")]
    [InlineData("Review")]
    public async Task Real_intake_post_commit_response_cannot_replace_saved_quantity_or_draft_outcome(string action)
    {
        await using var database=new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed=await database.SeedInventoryCatalogAsync(); int documentId,itemId,unitId,categoryId;
        await using (var db=database.CreateTenantContext(seed.StoreId))
        {
            var unit=await db.Units.SingleAsync(); unit.Name="kg"; unitId=unit.Id; categoryId=(await db.Categories.SingleAsync()).Id;
            var document=new StockDocument { StoreId=seed.StoreId, WarehouseId=seed.WarehouseId, DocumentNo="MONITOR-INTAKE", DocumentDate=DateTime.Today, Type=StockDocumentType.Receipt };
            document.ProvisionalItems.Add(new() { StoreId=seed.StoreId, NameSnapshot="Saved intake", UnitId=unitId, UnitNameSnapshot="kg", Quantity=1,
                ProposedFactor=1, ProposedBaseUnitId=unitId, ProposedBaseUnitName="kg", ProposedCategoryId=categoryId, Note="PRIVATE ITEM NOTE" });
            db.Add(document); await db.SaveChangesAsync(); documentId=document.Id; itemId=document.ProvisionalItems.Single().Id;
        }
        var tenant=new TenantContext(); tenant.SetStore(seed.StoreId,"intake"); var registry=new StoreActivityRegistry(TimeProvider.System);
        var committed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterPublished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aDb=ActorDb(database,tenant,101,new AfterCommitBarrier(async () => { committed.SetResult(); await laterPublished.Task.WaitAsync(TimeSpan.FromSeconds(20)); }));
        var serviceA=IntakeService(aDb,tenant,101); var initial=await serviceA.GetAsync(documentId); var initialItem=Assert.Single(initial.Items);
        ProvisionalReceivingMutationRequest request=action == "Quantity"
            ? new UpdateReceiptIntakeQuantityRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=initial.DocumentRowVersion,ItemRowVersion=initialItem.RowVersion,Quantity=2 }
            : new ReviewReceiptIntakeRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=initial.DocumentRowVersion,ItemRowVersion=initialItem.RowVersion,SaveDraftOnly=true,
                Completion=new() { Name="PRIVATE DRAFT NAME",UnitId=unitId,BaseUnitId=unitId,Factor=1,Quantity=999,CategoryId=categoryId,PurchasePrice=888,Note="PRIVATE DRAFT NOTE" } };
        var originalIntent=CopyIntent(request); var originalJson=JsonSerializer.Serialize(originalIntent,originalIntent.GetType());
        Assert.NotSame(request,originalIntent);
        if (request is ReviewReceiptIntakeRequest reviewIntent) Assert.NotSame(reviewIntent.Completion,((ReviewReceiptIntakeRequest)originalIntent).Completion);
        Task<ProvisionalReceivingStateDto> Invoke(StockDocumentProvisionalItemService service, ProvisionalReceivingMutationRequest intent) => intent is UpdateReceiptIntakeQuantityRequest quantity
            ? service.UpdateQuantityAsync(documentId,itemId,quantity,default)
            : service.ReviewIntakeAsync(documentId,itemId,(ReviewReceiptIntakeRequest)intent,new(true,true,true,true),default);
        // Draft saves retain the item's actual quantity; its private completion quantity is not a mutation fact.
        var savedQuantity=action == "Quantity" ? 2m : 1m;
        var a=MonitorMutation(aDb,tenant,registry,101,"ReceiptIntake",action,new() { ["documentId"]=documentId,["itemId"]=itemId,["request"]=request },async () =>
        {
            var response=await Invoke(serviceA,request); Assert.Equal(3m,Assert.Single(response.Items).Quantity);
            return new OkObjectResult(response);
        });
        await committed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var bDb=ActorDb(database,tenant,202); var serviceB=IntakeService(bDb,tenant,202); var current=await serviceB.GetAsync(documentId); var row=Assert.Single(current.Items);
            Assert.Equal(savedQuantity,row.Quantity);
            var bRequest=new UpdateReceiptIntakeQuantityRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=current.DocumentRowVersion,ItemRowVersion=row.RowVersion,Quantity=3 };
            await MonitorMutation(bDb,tenant,registry,202,"ReceiptIntake","Quantity",new() { ["documentId"]=documentId,["itemId"]=itemId,["request"]=bRequest },async () => new OkObjectResult(await serviceB.UpdateQuantityAsync(documentId,itemId,bRequest,default)));
        }
        finally { laterPublished.SetResult(); }
        await a;
        var eventA=Assert.Single(registry.Snapshot(seed.StoreId).Events,x => x.UserId == 101); var eventB=Assert.Single(registry.Snapshot(seed.StoreId).Events,x => x.UserId == 202);
        Assert.Contains(action == "Quantity" ? "1 kg → 2 kg" : "Số lượng 1 kg",eventA.Detail);
        Assert.DoesNotContain("3 kg",eventA.Detail); Assert.Contains($"{savedQuantity} kg → 3 kg",eventB.Detail);
        if (action == "Review") Assert.Contains("lưu thông tin chờ duyệt Saved intake",eventA.Text);
        Assert.All(registry.Snapshot(seed.StoreId).Events,x => { Assert.Equal($"receipt:{documentId}",x.WorkKey); Assert.DoesNotContain("PRIVATE",x.Detail); Assert.DoesNotContain("999",x.Detail); Assert.DoesNotContain("888",x.Detail); });
        Assert.Equal(originalJson,JsonSerializer.Serialize(originalIntent,originalIntent.GetType()));
        if (request is ReviewReceiptIntakeRequest normalizedReview)
        {
            Assert.Equal("kg",normalizedReview.Completion!.UnitName);
            Assert.Null(((ReviewReceiptIntakeRequest)originalIntent).Completion!.UnitName);
        }
        await using var replayDb=ActorDb(database,tenant,101); var replayService=IntakeService(replayDb,tenant,101);
        var replayIntent=CopyIntent(originalIntent);
        await MonitorMutation(replayDb,tenant,registry,101,"ReceiptIntake",action,new() { ["documentId"]=documentId,["itemId"]=itemId,["request"]=replayIntent },async () => new OkObjectResult(await Invoke(replayService,replayIntent)));
        Assert.Equal(2,registry.Snapshot(seed.StoreId).Events.Count);
        if (request is UpdateReceiptIntakeQuantityRequest original)
        {
            await using var conflictDb=ActorDb(database,tenant,101); var conflictService=IntakeService(conflictDb,tenant,101);
            var conflict=new UpdateReceiptIntakeQuantityRequest { CommandId=original.CommandId,DocumentRowVersion=original.DocumentRowVersion,ItemRowVersion=original.ItemRowVersion,Quantity=4 };
            await MonitorMutation(conflictDb,tenant,registry,101,"ReceiptIntake","Quantity",new() { ["documentId"]=documentId,["itemId"]=itemId,["request"]=conflict },async () =>
            { await Assert.ThrowsAsync<BusinessRuleException>(() => conflictService.UpdateQuantityAsync(documentId,itemId,conflict,default)); return new ConflictObjectResult(new { success=false }); });
            Assert.Equal(2,registry.Snapshot(seed.StoreId).Events.Count);
        }
    }
    [Theory]
    [InlineData("Capture")]
    [InlineData("Known")]
    public async Task Real_accumulation_and_known_link_keep_saved_quantity_when_recent_receipts_read_later_actor(string action)
    {
        await using var database=new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed=await database.SeedInventoryCatalogAsync(); int documentId,unitId,categoryId,conversionId,itemId;
        await using (var db=database.CreateTenantContext(seed.StoreId))
        {
            var unit=await db.Units.SingleAsync(); unit.Name="kg"; unitId=unit.Id; categoryId=(await db.Categories.SingleAsync()).Id;
            var product=await db.Products.SingleAsync(); product.Name="Saved intake";
            var conversion=new ProductUnitConversion { StoreId=seed.StoreId,ProductVariantId=seed.ProductVariantId,UnitId=unitId,Factor=1,IsActive=true };
            db.Add(conversion); await db.SaveChangesAsync(); conversionId=conversion.Id;
            var document=new StockDocument { StoreId=seed.StoreId,WarehouseId=seed.WarehouseId,DocumentNo="MONITOR-RECENT",DocumentDate=DateTime.Today,Type=StockDocumentType.Receipt };
            document.ProvisionalItems.Add(new() { StoreId=seed.StoreId,NameSnapshot="Saved intake",RawBarcodeSnapshot="MONITOR-NEW",NormalizedBarcode="MONITOR-NEW",UnitId=unitId,UnitNameSnapshot="kg",NormalizedUnitNameSnapshot="KG",Quantity=1,ProposedFactor=1,ProposedBaseUnitId=unitId,ProposedBaseUnitName="kg",ProposedCategoryId=categoryId });
            if (action == "Known") document.Lines.Add(new() { ProductVariantId=seed.ProductVariantId,ProductUnitConversionId=conversionId,ProductNameSnapshot="Saved intake",UnitId=unitId,UnitNameSnapshot="kg",Quantity=1,BaseQuantity=1,Factor=1,LineNo=1 });
            db.Add(document); await db.SaveChangesAsync(); documentId=document.Id; itemId=document.ProvisionalItems.Single().Id;
        }
        var tenant=new TenantContext(); tenant.SetStore(seed.StoreId,"recent"); var registry=new StoreActivityRegistry(TimeProvider.System);
        await using var aDb=ActorDb(database,tenant,101); var serviceA=IntakeService(aDb,tenant,101); var state=await serviceA.GetAsync(documentId);
        ProvisionalReceivingMutationRequest request=action == "Capture"
            ? new CaptureReceiptIntakeRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=state.DocumentRowVersion,Name="Saved intake",Barcode="MONITOR-NEW",UnitId=unitId,BaseUnitId=unitId,Factor=1,Quantity=1,CategoryId=categoryId,Note="PRIVATE CAPTURE" }
            : new RecordKnownReceiptItemRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=state.DocumentRowVersion,ProductUnitConversionId=conversionId,Factor=1,Quantity=1,Note="PRIVATE KNOWN" };
        var originalIntent=CopyIntent(request); var originalJson=JsonSerializer.Serialize(originalIntent,originalIntent.GetType());
        Assert.NotSame(request,originalIntent);
        Task<ProvisionalReceivingStateDto> Invoke(StockDocumentProvisionalItemService service, ProvisionalReceivingMutationRequest intent) => intent is CaptureReceiptIntakeRequest capture
            ? service.CaptureIntakeAsync(documentId,capture,new(true,true,true,true),default)
            : service.RecordKnownAsync(documentId,(RecordKnownReceiptItemRequest)intent,default);
        await MonitorMutation(aDb,tenant,registry,101,"ReceiptIntake",action,new() { ["documentId"]=documentId,["request"]=request },async () =>
        {
            var response=await Invoke(serviceA,request);
            await using var bDb=ActorDb(database,tenant,202);
            if (action == "Capture")
            {
                var serviceB=IntakeService(bDb,tenant,202); var current=await serviceB.GetAsync(documentId); var row=Assert.Single(current.Items,x => x.Id == itemId); Assert.Equal(2m,row.Quantity);
                var bRequest=new UpdateReceiptIntakeQuantityRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=current.DocumentRowVersion,ItemRowVersion=row.RowVersion,Quantity=3 };
                await MonitorMutation(bDb,tenant,registry,202,"ReceiptIntake","Quantity",new() { ["documentId"]=documentId,["itemId"]=itemId,["request"]=bRequest },async () => new OkObjectResult(await serviceB.UpdateQuantityAsync(documentId,itemId,bRequest,default)));
            }
            else
            {
                var line=await bDb.StockDocumentLines.AsNoTracking().SingleAsync(x => x.StockDocumentId == documentId); Assert.Equal(2m,line.Quantity);
                var bRequest=new UpdateStockDocumentLineRequest { UnitId=unitId,Quantity=3 };
                await MonitorMutation(bDb,tenant,registry,202,"StockDocuments","UpdateLine",new() { ["documentId"]=documentId,["lineId"]=line.Id,["request"]=bRequest },async () =>
                { await ReceiptService(bDb,tenant,202).UpdateLineAsync(line.Id,bRequest); return new OkObjectResult(new { success=true }); });
            }
            response.RecentReceipts=await serviceA.GetRecentAsync(documentId,default);
            Assert.Equal(3m,Assert.Single(response.RecentReceipts,x => x.CommandId == request.CommandId).CurrentQuantity);
            return new OkObjectResult(response);
        });
        var first=Assert.Single(registry.Snapshot(seed.StoreId).Events,x => x.UserId == 101);
        Assert.Contains("Saved intake",first.Text); Assert.Contains("Số lượng 2 kg",first.Detail); Assert.DoesNotContain("3 kg",first.Detail);
        Assert.All(registry.Snapshot(seed.StoreId).Events,x => { Assert.Equal($"receipt:{documentId}",x.WorkKey); Assert.DoesNotContain("PRIVATE",x.Detail); });
        Assert.Equal(originalJson,JsonSerializer.Serialize(originalIntent,originalIntent.GetType()));
        if (request is CaptureReceiptIntakeRequest normalizedCapture)
        {
            Assert.Equal("kg",normalizedCapture.UnitName); Assert.Equal("kg",normalizedCapture.BaseUnitName);
            Assert.Null(((CaptureReceiptIntakeRequest)originalIntent).UnitName); Assert.Null(((CaptureReceiptIntakeRequest)originalIntent).BaseUnitName);
        }
        await using var replayDb=ActorDb(database,tenant,101); var replayService=IntakeService(replayDb,tenant,101);
        var replayIntent=CopyIntent(originalIntent);
        await MonitorMutation(replayDb,tenant,registry,101,"ReceiptIntake",action,new() { ["documentId"]=documentId,["request"]=replayIntent },async () =>
        { var replay=await Invoke(replayService,replayIntent); replay.RecentReceipts=await replayService.GetRecentAsync(documentId,default); return new OkObjectResult(replay); });
        Assert.Equal(2,registry.Snapshot(seed.StoreId).Events.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_review_reports_the_saved_removal_or_resolution_and_replay_has_no_animation(bool approve)
    {
        await using var database=new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed=await database.SeedInventoryCatalogAsync(); int documentId,itemId,unitId;
        await using (var db=database.CreateTenantContext(seed.StoreId))
        {
            var unit=await db.Units.SingleAsync(); unit.Name="kg"; unitId=unit.Id;
            (await db.Products.SingleAsync()).Name="Reviewed rice";
            var conversion=new ProductUnitConversion { StoreId=seed.StoreId,ProductVariantId=seed.ProductVariantId,UnitId=unitId,Factor=1,IsActive=true };
            db.Add(conversion); await db.SaveChangesAsync();
            var document=new StockDocument { StoreId=seed.StoreId,WarehouseId=seed.WarehouseId,DocumentNo="MONITOR-REVIEW",DocumentDate=DateTime.Today,Type=StockDocumentType.Receipt };
            document.ProvisionalItems.Add(new() { StoreId=seed.StoreId,NameSnapshot="Captured rice",UnitId=unitId,UnitNameSnapshot="kg",Quantity=2,
                ProposedProductVariantId=seed.ProductVariantId,ProposedBaseUnitId=unitId,ProposedBaseUnitName="kg",ProposedFactor=1,Note="PRIVATE REVIEW NOTE" });
            db.Add(document); await db.SaveChangesAsync(); documentId=document.Id; itemId=document.ProvisionalItems.Single().Id;
        }
        var tenant=new TenantContext(); tenant.SetStore(seed.StoreId,"review"); var registry=new StoreActivityRegistry(TimeProvider.System);
        await using var actorDb=ActorDb(database,tenant,101); var service=IntakeService(actorDb,tenant,101); var initial=await service.GetAsync(documentId); var item=Assert.Single(initial.Items);
        var request=new ReviewReceiptIntakeRequest { CommandId=Guid.NewGuid(),DocumentRowVersion=initial.DocumentRowVersion,ItemRowVersion=item.RowVersion,Approve=approve };
        var args=new Dictionary<string,object?> { ["documentId"]=documentId,["itemId"]=itemId,["request"]=request };
        await MonitorMutation(actorDb,tenant,registry,101,"ReceiptIntake","Review",args,async () => new OkObjectResult(await service.ReviewIntakeAsync(documentId,itemId,request,new(true,true,true,true),default)));
        var activity=Assert.Single(registry.Snapshot(seed.StoreId).Events);
        Assert.Contains(approve ? "vừa duyệt mặt hàng Reviewed rice" : "vừa bỏ Captured rice khỏi phiếu nhập",activity.Text);
        Assert.Contains(approve ? "Số lượng 2 kg" : "Đã bỏ 2 kg",activity.Detail); Assert.DoesNotContain("PRIVATE",activity.Detail);
        Assert.Equal($"receipt:{documentId}",activity.WorkKey); Assert.Equal("ReceiptIntake.Review",activity.Action);
        await using var inspect=database.CreateTenantContext(seed.StoreId);
        var saved=await inspect.StockDocumentProvisionalItems.SingleAsync(x => x.Id == itemId);
        Assert.Equal(approve ? StockDocumentProvisionalItemStatus.Resolved : StockDocumentProvisionalItemStatus.Removed,saved.Status);
        Assert.Equal(approve,await inspect.StockDocumentLines.AnyAsync(x => x.StockDocumentId == documentId));
        await using var replayDb=ActorDb(database,tenant,101); var replay=IntakeService(replayDb,tenant,101);
        await MonitorMutation(replayDb,tenant,registry,101,"ReceiptIntake","Review",args,async () => new OkObjectResult(await replay.ReviewIntakeAsync(documentId,itemId,request,new(true,true,true,true),default)));
        Assert.Single(registry.Snapshot(seed.StoreId).Events);
    }
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
            // Reproduce the old pre-filter snapshot while the service has no tracked line yet.
            Assert.Empty(bDb.ChangeTracker.Entries<StockDocumentLine>());
            var prior=await StoreActivityEnricher.Read(bDb,seed.StoreId,"StockDocuments","UpdateLine",
                new Dictionary<string,object?> { ["documentId"]=documentId,["lineId"]=lineId },null,null,default);
            Assert.Equal(1m,prior!.Line!.Quantity); Assert.Equal(10m,prior.Line.SavedCost);
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
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var document=await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            document.HasFreight=true; document.FreightTotal=9; document.FreightPayeeName="Prior payee";
            await db.SaveChangesAsync(); receiptVersion=Convert.ToBase64String(document.RowVersion);
        }
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

using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceProductPickerSqlTests
{
    [Fact]
    public async Task Picker_filters_documentary_stock_before_limit_resolves_invoice_warehouse_and_rechecks_locked_adds_in_SQL()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(); var foreign = await database.SeedInventoryCatalogAsync();
        int invoiceId, positiveId, fractionalId, correctionId, heldId, currentDetailId, otherWarehouseId;
        long firstSearchMilliseconds=0;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
            var legal = await db.LegalEntities.SingleAsync(x => x.Id == warehouse.LegalEntityId); legal.DefaultWarehouseId = warehouse.Id;
            var otherWarehouse = new Warehouse { StoreId = seed.StoreId, LegalEntityId = legal.Id, Code = "PICKER-OTHER", Name = "Kho khác", IsActive = true };
            db.Add(otherWarehouse); await db.SaveChangesAsync(); otherWarehouseId = otherWarehouse.Id;
            var invoice = Head(seed.StoreId, legal.Id); var held = Head(seed.StoreId, legal.Id); held.ProviderStatus = InvoiceProviderStatus.Issuing;
            var issued = Head(seed.StoreId, legal.Id); issued.ProviderStatus = InvoiceProviderStatus.Issued; issued.ProviderInvoiceNo = "PICKER-ISSUED";
            db.AddRange(invoice, held, issued); await db.SaveChangesAsync(); invoiceId = invoice.Id; heldId = held.Id;
            var variants = new List<ProductVariant>();
            var seedProductId = await db.ProductVariants.Where(x=>x.Id==seed.ProductVariantId).Select(x=>x.ProductId).SingleAsync();
            for (var i = 0; i < 2200; i++) { var variant = new ProductVariant { StoreId = seed.StoreId, ProductId = seedProductId,
                Sku = $"PICKER-{i:00}", ProductVariantName = $"Hoa {i:00}", ProductVariantNameNormalized = $"hoa {i:00}", Price = 28000,
                IsActive = i != 30, HasInputInvoice = false }; variants.Add(variant); db.Add(variant); }
            await db.SaveChangesAsync(); positiveId = variants[25].Id; fractionalId = variants[26].Id;
            void Opening(int variantId, decimal qty, int? wh = null) => db.Add(new InvoiceInputStockSupplementalMovement { StoreId = seed.StoreId,
                WarehouseId = wh ?? warehouse.Id, ProductVariantId = variantId, QuantityChange = qty, EffectiveAtUtc = DateTime.UtcNow,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening, LegacySourceKey = "PICKER-" + variantId + "-" + (wh ?? warehouse.Id) });
            Opening(positiveId,12); Opening(fractionalId,.5m); Opening(variants[27].Id,-1); Opening(variants[28].Id,99,otherWarehouse.Id);
            Opening(variants[30].Id,4); Opening(variants[31].Id,4); variants[31].IsDeleted = true;
            Opening(variants[32].Id,5); Opening(variants[33].Id,10); Opening(variants[34].Id,10);
            db.Add(Line(issued.Id, seed.StoreId, positiveId,2)); db.Add(Line(held.Id,seed.StoreId,positiveId,3));
            db.Add(Line(held.Id,seed.StoreId,variants[33].Id,10)); db.Add(Line(invoice.Id,seed.StoreId,variants[34].Id,10));
            var currentLine = Line(invoice.Id,seed.StoreId,positiveId,1); db.Add(currentLine);
            var conversion = new ProductUnitConversion { StoreId = seed.StoreId, ProductVariantId = positiveId,
                UnitId = (await db.Products.SingleAsync(x=>x.Id==seedProductId)).BaseUnitId, Factor = 1, IsBaseUnit = true, IsActive = true };
            db.Add(new ProductVariantUnitBarcode { StoreId = seed.StoreId, ProductUnitConversion = conversion, Barcode = "8900123456789", IsActive = true });
            await db.SaveChangesAsync(); currentDetailId = currentLine.Id;
            var correction = Head(seed.StoreId,legal.Id); correction.OriginalInvoiceHeadId=invoice.Id; correction.CorrectionType=InvoiceCorrectionType.AdjustmentAmount;
            db.Add(correction); await db.SaveChangesAsync(); correctionId=correction.Id;
            db.ChangeTracker.Clear();

            var repo = new InvoiceInputStockRepository(db);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var page = await repo.SearchProductsAsync(invoiceId,"hoa",20);firstSearchMilliseconds=clock.ElapsedMilliseconds;
            Assert.True(page.IsSuccess,page.Error.Message); Assert.True(page.Value.StockFilterApplied); Assert.Equal(warehouse.Id,page.Value.WarehouseId);
            Assert.Equal(3,page.Value.Items.Count); Assert.Contains(page.Value.Items,x=>x.ProductVariantId==positiveId);
            Assert.Contains(page.Value.Items,x=>x.ProductVariantId==fractionalId && x.AvailableQuantity==.5m);
            Assert.DoesNotContain(page.Value.Items,x=>x.ProductVariantId==variants[33].Id || x.ProductVariantId==variants[34].Id);
            var item=Assert.Single(page.Value.Items,x=>x.ProductVariantId==positiveId);
            Assert.Equal(7m,item.AvailableQuantity);Assert.Equal(1m,item.AlreadyOnInvoiceQuantity);Assert.Equal(6m,item.MaximumAdditionalQuantity);
            Assert.Equal("8900123456789",item.Barcode);
            var ledger = InvoiceInputStockReadService.BuildPage(await new InvoiceInputStockReadRepository(db).GetPageMovementsAsync(seed.StoreId,[positiveId],[warehouse.Id]),new());
            Assert.Equal(Assert.Single(ledger.Balances).Available,item.AvailableQuantity);
            Assert.Equal(positiveId,Assert.Single((await repo.SearchProductsAsync(invoiceId,"8900123456789")).Value.Items).ProductVariantId);
            Assert.Equal(positiveId,Assert.Single((await repo.SearchProductsAsync(invoiceId,"PICKER-25")).Value.Items).ProductVariantId);
            Assert.Empty((await repo.SearchProductsAsync(invoiceId,"%'<script>")).Value.Items);
            Assert.True((await repo.SearchProductsAsync(invoiceId,new string('x',201))).IsFailure);
            Assert.Empty(db.ChangeTracker.Entries());

            // Imported drafts may already have a linked historical outbound. It must
            // not consume the current lines a second time in the additional limit.
            (await db.InvoiceHeads.SingleAsync(x=>x.Id==invoiceId)).LegacySourceId=999999;
            var legacyOutbound=new InvoiceInputStockSupplementalMovement{StoreId=seed.StoreId,WarehouseId=warehouse.Id,ProductVariantId=positiveId,
                QuantityChange=-1,MovementType=InvoiceInputStockSupplementalMovementType.LegacyOutbound,EffectiveAtUtc=DateTime.UtcNow,
                LegacySourceKey="PICKER-CURRENT-LEGACY-OUT",LegacyOrderId=999999};
            db.Add(legacyOutbound);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var legacyStock=(await repo.GetProductStockAsync(invoiceId,positiveId)).Value;
            Assert.Equal(6m,legacyStock.AvailableQuantity);Assert.Equal(6m,legacyStock.MaximumAdditionalQuantity);
            (await db.InvoiceInputStockSupplementalMovements.SingleAsync(x=>x.Id==legacyOutbound.Id)).IsDeleted=true;
            (await db.InvoiceHeads.SingleAsync(x=>x.Id==invoiceId)).LegacySourceId=null;
            await db.SaveChangesAsync();db.ChangeTracker.Clear();

            (await db.InvoiceDetails.SingleAsync(x=>x.Id==currentDetailId)).LegacyUnitFactor=4;await db.SaveChangesAsync();db.ChangeTracker.Clear();
            Assert.Equal(3m,(await repo.GetProductStockAsync(invoiceId,positiveId)).Value.MaximumAdditionalQuantity);
            (await db.InvoiceDetails.SingleAsync(x=>x.Id==currentDetailId)).LegacyUnitFactor=null;
            (await db.InvoiceDetails.SingleAsync(x=>x.InvoiceHeadId==heldId&&x.ProductVariantId==positiveId)).Quantity=4;
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var before=await db.InvoiceDetails.CountAsync();
            var rejected=await Command(db).AddManualDetailAsync(Request(invoiceId,positiveId,6));
            Assert.True(rejected.IsFailure);Assert.Equal("Invoice.InputStockInsufficient",rejected.Error.Code);
            Assert.Equal(before,await db.InvoiceDetails.CountAsync());
            db.ChangeTracker.Clear();
            var added=await Command(db).AddManualDetailAsync(Request(invoiceId,positiveId,1));Assert.True(added.IsSuccess,added.Error.Message);
            db.ChangeTracker.Clear();Assert.Equal(4m,(await repo.GetProductStockAsync(invoiceId,positiveId)).Value.MaximumAdditionalQuantity);
            Assert.True((await Command(db).AddManualDetailAsync(new(){InvoiceHeadId=invoiceId,ItemName="Dịch vụ nhập tay",Quantity=1,UnitName="Lần",UnitPrice=1000})).IsSuccess);
            db.ChangeTracker.Clear();
            var correctionPage=await repo.SearchProductsAsync(correctionId,"hoa");Assert.True(correctionPage.IsSuccess);Assert.False(correctionPage.Value.StockFilterApplied);
            Assert.Equal(20,correctionPage.Value.Items.Count);Assert.All(correctionPage.Value.Items,x=>Assert.Null(x.AvailableQuantity));
            Assert.True((await Command(db).AddManualDetailAsync(Request(correctionId,positiveId,-1))).IsSuccess);
            db.ChangeTracker.Clear();
            Assert.Equal(0,(await repo.GetProductStockAsync(invoiceId,variants[28].Id)).Value.AvailableQuantity);
            Assert.True((await repo.GetProductStockAsync(invoiceId,foreign.ProductVariantId)).IsFailure);
            Assert.Empty(await db.InventoryTransactions.ToListAsync());Assert.Empty(await db.InventoryBalances.ToListAsync());
        }
        // Same-invoice additions serialize behind the existing store lock: only one
        // request can consume the last four base units in the editable draft.
        async Task<bool> AddLast() { await using var db=database.CreateTenantContext(seed.StoreId);return (await Command(db).AddManualDetailAsync(Request(invoiceId,positiveId,4))).IsSuccess; }
        var concurrent=await Task.WhenAll(AddLast(),AddLast());Assert.Equal(1,concurrent.Count(x=>x));
        await using(var db=database.CreateTenantContext(seed.StoreId))
        {
            Assert.Equal(6m,await db.InvoiceDetails.Where(x=>x.InvoiceHeadId==invoiceId&&x.ProductVariantId==positiveId).SumAsync(x=>x.Quantity));
            var repo=new InvoiceInputStockRepository(db);Assert.DoesNotContain((await repo.SearchProductsAsync(invoiceId,"hoa")).Value.Items,x=>x.ProductVariantId==positiveId);
            var warehouse=await db.Warehouses.SingleAsync(x=>x.Id==seed.WarehouseId);
            (await db.LegalEntities.SingleAsync(x=>x.Id==warehouse.LegalEntityId)).DefaultWarehouseId=null;
            foreach(var wh in await db.Warehouses.ToListAsync())wh.IsDefault=false;
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var missing=await repo.SearchProductsAsync(invoiceId,"hoa");Assert.True(missing.IsFailure);Assert.Equal("Invoice.PickerWarehouseMissing",missing.Error.Code);
            Assert.False(db.Database.HasPendingModelChanges());Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        await using(var db=database.CreateTenantContext(foreign.StoreId))Assert.True((await new InvoiceInputStockRepository(db).SearchProductsAsync(invoiceId,"hoa")).IsFailure);
        await using(var db=database.CreateHostContext())Assert.True((await new InvoiceInputStockRepository(db).SearchProductsAsync(invoiceId,"hoa")).IsFailure);
        var output=Path.Combine(GaoApp.Tests.Security.FullApplicationFixture.SourceRoot(),"TestResults","invoice-product-picker-20261029");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"repository.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=true,matchingCandidates=2200,eligibleResults=3,firstSearchMilliseconds,
            filtersBeforeTake=true,documentaryNotPhysicalStock=true,legacyOutboundAndUnitFactorsHandled=true,concurrentLastAddWinners=1,
            foreignTenantAndHostContextRejected=true,missingWarehouseHandled=true,correctionsAndFreeTextPreserved=true,noPendingMigrations=true,fixtureOnly=true},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }

    private static InvoiceHead Head(int store,int legal)=>new(){StoreId=store,LegalEntityId=legal,IsAutoInvoiceGroup=true,ProviderStatus=InvoiceProviderStatus.LocalDraft};
    private static InvoiceDetail Line(int head,int store,int variant,decimal qty)=>new(){StoreId=store,InvoiceHeadId=head,ProductVariantId=variant,ItemName="Hoa",UnitName="Cái",SourceType=InvoiceDetailSourceType.Manual,Quantity=qty,UnitPrice=28000};
    private static CreateManualInvoiceDetailRequest Request(int head,int variant,decimal qty)=>new(){InvoiceHeadId=head,ProductVariantId=variant,Quantity=qty,UnitPrice=28000,UnitName="Cái",ItemName="Hoa"};
    private static InvoiceCommandService Command(AppDbContext db)=>new(new InvoiceRepository(db),new ProductVariantRepository(db),null!,null!,null!,new InvoiceInputStockRepository(db));
}

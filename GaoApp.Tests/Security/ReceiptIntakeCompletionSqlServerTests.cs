using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptIntakeCompletionSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/intake";
    private static ReviewReceiptIntakeRequest Request(JsonElement state, int itemId, ReceiptIntakeCompletionDto draft, bool approve = false) => new() {
        CommandId = Guid.NewGuid(), DocumentRowVersion = state.GetProperty("documentRowVersion").GetString()!,
        ItemRowVersion = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == itemId).GetProperty("rowVersion").GetString()!,
        Approve = approve, SaveDraftOnly = !approve, Completion = draft
    };
    [Fact]
    public async Task Draft_survives_reload_keeps_original_then_approval_uses_employee_code_and_corrected_packing_atomically()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            throw new PlatformNotSupportedException("Receipt completion photo fixtures require Windows System.Drawing.");
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await ReceiptIntakeSupplierSqlServerTests.SeedAsync(app, store);
        int unit;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
            (await db.StockDocuments.SingleAsync(x=>x.Id==seed.ReceiptId)).SupplierId=seed.SupplierId;
            unit=(await db.Products.SingleAsync()).BaseUnitId; await db.SaveChangesAsync();
        }
        using var manager=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using var employee=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Inventory.StockDocument.Update));
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        var url=Url(seed.ReceiptId); var state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var draft=new ReceiptIntakeCompletionDto {Name="Bánh đã kiểm tra",Barcode="00109238190832",BaseUnitId=unit,
            UnitName="Thùng kiểm tra",Factor=24,Quantity=2,CategoryId=seed.CategoryId,
            PurchasePrice=240000,RetailPrice=300000,WholesalePrice=288000,IsSellable=true};
        using var bitmap=new System.Drawing.Bitmap(64,64);
        using var photoStream=new MemoryStream();bitmap.Save(photoStream,System.Drawing.Imaging.ImageFormat.Jpeg);
        var photo=photoStream.ToArray();
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
            (await db.StockDocumentProvisionalItems.SingleAsync(x=>x.Id==seed.ItemIds[0])).PackagingPhoto=photo;
            await db.SaveChangesAsync();
        }
        state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var request=Request(state,seed.ItemIds[0],draft);
        request.PhotoDataUrl="data:image/jpeg;base64,"+Convert.ToBase64String(photo);
        var review=url+$"/{seed.ItemIds[0]}/review";
        using(var denied=await employee.Http.PostAsJsonAsync(review,request)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await foreign.Http.PostAsJsonAsync(review,request)) Assert.False(denied.IsSuccessStatusCode);
        var invalid=Request(state,seed.ItemIds[0],draft);invalid.PhotoDataUrl="data:image/jpeg;base64,invalid";
        using(var rejected=await manager.Http.PostAsJsonAsync(review,invalid)) Assert.False(rejected.IsSuccessStatusCode);
        await manager.JsonAsync(HttpMethod.Post,review,request);
        await manager.JsonAsync(HttpMethod.Post,review,request); // retry is idempotent
        state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var item=state.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==seed.ItemIds[0]);
        Assert.True(item.GetProperty("hasReviewPhoto").GetBoolean());
        Assert.Equal(photo,await manager.Http.GetByteArrayAsync(url+$"/{seed.ItemIds[0]}/review-photo"));
        Assert.Equal(photo,await manager.Http.GetByteArrayAsync(url+$"/{seed.ItemIds[0]}/photo"));
        using(var denied=await foreign.Http.GetAsync(url+$"/{seed.ItemIds[0]}/review-photo")) Assert.False(denied.IsSuccessStatusCode);
        Assert.Equal("Bánh đã kiểm tra",item.GetProperty("reviewDraft").GetProperty("name").GetString());
        Assert.Equal(seed.ProductNames[0],item.GetProperty("name").GetString());
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) Assert.False(await db.Products.AnyAsync(x=>x.Name==draft.Name));
        var stale=Request(state,seed.ItemIds[0],draft);stale.DocumentRowVersion=request.DocumentRowVersion;
        using(var rejected=await manager.Http.PostAsJsonAsync(review,stale)) Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
        var remove=Request(state,seed.ItemIds[0],draft);remove.RemoveReviewPhoto=true;
        await manager.JsonAsync(HttpMethod.Post,review,remove);
        using(var missing=await manager.Http.GetAsync(url+$"/{seed.ItemIds[0]}/review-photo"))Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var restore=Request(state,seed.ItemIds[0],draft);restore.PhotoDataUrl=request.PhotoDataUrl;
        await manager.JsonAsync(HttpMethod.Post,review,restore);
        state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var approve=Request(state,seed.ItemIds[0],draft,true);
        await manager.JsonAsync(HttpMethod.Post,review,approve);await manager.JsonAsync(HttpMethod.Post,review,approve);
        await using var check=app.Database.CreateTenantContext(store.StoreId);
        var product=await check.Products.SingleAsync(x=>x.Name==draft.Name);
        var image=Assert.Single(await check.ProductImages.Where(x=>x.ProductId==product.Id).ToListAsync());
        Assert.True(image.IsPrimary);
        Assert.Equal(photo,(await check.StockDocumentProvisionalItems.SingleAsync(x=>x.Id==seed.ItemIds[0])).PackagingPhoto);
        Assert.True(product.IsSellable);Assert.Equal(12500,product.BasePrice);Assert.Equal(unit,product.BaseUnitId);
        var variants=await check.ProductVariants.Where(x=>x.ProductId==product.Id).ToListAsync();var variant=Assert.Single(variants);
        Assert.Equal(10000,variant.CostPrice);
        var units=await check.ProductUnitConversions.Where(x=>x.ProductVariantId==variant.Id).ToListAsync();Assert.Equal(2,units.Count);
        var pack=units.Single(x=>x.Factor==24);Assert.Equal(300000,pack.Price);Assert.Equal(288000,pack.WholesalePrice);
        var code=Assert.Single(await check.ProductVariantUnitBarcodes.Where(x=>x.ProductUnitConversion.ProductVariantId==variant.Id).ToListAsync());
        Assert.Equal(draft.Barcode,code.Barcode);Assert.True(code.IsPrimary);Assert.Equal(pack.Id,code.ProductUnitConversionId);Assert.Equal(BarcodeType.External,code.BarcodeType);
        var line=await check.StockDocumentLines.SingleAsync(x=>x.StockDocumentId==seed.ReceiptId && x.ProductVariantId==variant.Id);
        Assert.Equal(48,line.BaseQuantity);Assert.Equal(240000,line.UnitPriceBeforeVat);Assert.Equal(480000,line.LineTotal);
        var evidence=await check.StockDocumentProvisionalItems.SingleAsync(x=>x.Id==seed.ItemIds[0]);
        Assert.NotNull(evidence.OriginalDeclarationJson);Assert.Contains(seed.ProductNames[0],JsonSerializer.Deserialize<JsonElement>(evidence.OriginalDeclarationJson!).GetProperty("NameSnapshot").GetString());
        Assert.Equal(StockDocumentStatus.PendingApproval,(await check.StockDocuments.SingleAsync(x=>x.Id==seed.ReceiptId)).Status);
        Assert.Equal(100,(await check.InventoryBalances.SingleAsync()).OnHandQty);Assert.Empty(await check.PurchasePayables.ToListAsync());
    }

    [Fact]
    public async Task Conflicting_barcode_rolls_back_and_link_existing_avoids_duplicate_product_while_empty_code_generates_once()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        var seed=await ReceiptIntakeSupplierSqlServerTests.SeedAsync(app,store);
        int unit,variantId;string unitName;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
            (await db.StockDocuments.SingleAsync(x=>x.Id==seed.ReceiptId)).SupplierId=seed.SupplierId;
            (await db.StockDocuments.SingleAsync(x=>x.Id==seed.ReceiptId)).HasVat=true;
            var product=await db.Products.Include(x=>x.BaseUnit).SingleAsync();unit=product.BaseUnitId;unitName=product.BaseUnit.Name;variantId=store.VariantId;
            await db.SaveChangesAsync();
        }
        using var manager=await app.LoginAsync(await app.AddAccountAsync(store,"*"));var url=Url(seed.ReceiptId);
        var state=(await manager.JsonAsync(HttpMethod.Get,url)).GetProperty("state");
        var draft=new ReceiptIntakeCompletionDto{Name="Không mã",BaseUnitId=unit,UnitId=unit,Factor=1,Quantity=3,CategoryId=seed.CategoryId};
        state=await manager.JsonAsync(HttpMethod.Post,url+$"/{seed.ItemIds[0]}/review",Request(state,seed.ItemIds[0],draft,true));
        int newVariant,conversionId;string code;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
            var barcode=Assert.Single(await db.ProductVariantUnitBarcodes.Where(x=>x.ProductUnitConversion.ProductVariant.Product.Name==draft.Name).ToListAsync());
            Assert.Equal(BarcodeType.Internal,barcode.BarcodeType);Assert.True(barcode.IsPrimary);code=barcode.Barcode;conversionId=barcode.ProductUnitConversionId;
            newVariant=(await db.ProductUnitConversions.SingleAsync(x=>x.Id==conversionId)).ProductVariantId;
            (await db.StockDocumentLines.SingleAsync(x=>x.StockDocumentId==seed.ReceiptId&&x.ProductVariantId==newVariant)).TaxRate=10;
            await db.SaveChangesAsync();
        }
        var found=await manager.JsonAsync(HttpMethod.Get,url+"/review-products?term="+Uri.EscapeDataString(code));
        Assert.Equal(conversionId,Assert.Single(found.GetProperty("results").EnumerateArray()).GetProperty("id").GetInt32());
        var unaccented=await manager.JsonAsync(HttpMethod.Get,url+"/review-products?term=Khong%20ma");
        Assert.Contains(unaccented.GetProperty("results").EnumerateArray(),x=>x.GetProperty("id").GetInt32()==conversionId);
        draft.Name="Không được tạo trùng";draft.Barcode=code;
        using(var conflict=await manager.Http.PostAsJsonAsync(url+$"/{seed.ItemIds[1]}/review",Request(state,seed.ItemIds[1],draft,true))) {
            Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.Contains("Barcode",await conflict.Content.ReadAsStringAsync());
        }
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) Assert.False(await db.Products.AnyAsync(x=>x.Name==draft.Name));
        // Same barcode + same unit can be linked explicitly; no new product or barcode.
        draft.ProductVariantId=newVariant;draft.UnitName=unitName;draft.BaseUnitName=unitName;draft.PurchasePrice=13000;
        await manager.JsonAsync(HttpMethod.Post,url+$"/{seed.ItemIds[1]}/review",Request(state,seed.ItemIds[1],draft,true));
        await using var check=app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(2,await check.Products.CountAsync());
        Assert.Equal(0,(await check.ProductVariants.SingleAsync(x=>x.Id==newVariant)).CostPrice);
        var inactive=await check.Products.SingleAsync(x=>x.Name=="Không mã");inactive.IsActive=false;await check.SaveChangesAsync();
        var inactiveResult=await manager.JsonAsync(HttpMethod.Get,url+"/review-products?term="+Uri.EscapeDataString(code));
        Assert.True(Assert.Single(inactiveResult.GetProperty("results").EnumerateArray()).GetProperty("disabled").GetBoolean());
        Assert.Single(await check.ProductVariantUnitBarcodes.Where(x=>x.ProductUnitConversionId==conversionId).ToListAsync());
        var mergedLine=await check.StockDocumentLines.SingleAsync(x=>x.StockDocumentId==seed.ReceiptId&&x.ProductVariantId==newVariant);
        Assert.Equal(6,mergedLine.Quantity);Assert.Equal(13000,mergedLine.UnitPriceBeforeVat);Assert.Equal(85800,mergedLine.LineTotal);
        Assert.Equal(100,(await check.InventoryBalances.SingleAsync()).OnHandQty);
    }
}

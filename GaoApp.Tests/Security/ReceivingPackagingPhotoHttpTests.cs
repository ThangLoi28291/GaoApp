using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GaoApp.Tests.Security;

public class ReceivingPackagingPhotoHttpTests
{
    private const string Jpeg = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/4gHYSUNDX1BST0ZJTEUAAQEAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADb/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAAYACADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAQH/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AnIBQxwAAAAAB/9k=";
    [Fact]
    public async Task Photo_is_transactional_idempotent_and_scoped_to_authorized_store_and_receipt()
    {
        await using var app=await FullApplicationFixture.StartAsync();
        var store=app.Stores[0];
        using var employee=await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.Create,PermissionCodes.Inventory.StockDocument.Update,PermissionCodes.Inventory.StockDocument.View));
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        using var denied=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Inventory.StockDocument.Create));
        int documentId,unitId,categoryId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            var product=await db.ProductVariants.Include(x=>x.Product).SingleAsync(x=>x.Id==store.VariantId);
            unitId=product.Product.BaseUnitId;categoryId=product.Product.CategoryId;
            var document=new StockDocument {StoreId=store.StoreId,DocumentNo="MOBILE-PHOTO",WarehouseId=store.WarehouseId,
                ReceiptSource=PurchaseReceiptSource.Direct,DirectReceiptReason="Kiểm thử ảnh bao bì",IsMerchandisePaid=true};
            db.StockDocuments.Add(document);await db.SaveChangesAsync();documentId=document.Id;
        }
        var url=$"/admin/api/stock-documents/{documentId}/intake";
        var config=await employee.JsonAsync(HttpMethod.Get,url);
        var request=new {commandId=Guid.NewGuid(),documentRowVersion=config.GetProperty("state").GetProperty("documentRowVersion").GetString(),
            name="Hàng có ảnh",barcode="MOBILE-PHOTO-01",baseUnitId=unitId,unitId,factor=1,quantity=2,categoryId,photoDataUrl=Jpeg};
        var state=await employee.JsonAsync(HttpMethod.Post,url,request);
        var replay=await employee.JsonAsync(HttpMethod.Post,url,request);
        var item=Assert.Single(replay.GetProperty("items").EnumerateArray());
        Assert.True(item.GetProperty("hasPhoto").GetBoolean());Assert.Equal(2,item.GetProperty("quantity").GetDecimal());
        var photoUrl=$"{url}/{item.GetProperty("id").GetInt32()}/photo";
        using var response=await employee.Http.GetAsync(photoUrl);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("image/jpeg",response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(Convert.FromBase64String(Jpeg.Split(',')[1]),await response.Content.ReadAsByteArrayAsync());
        using var forbidden=await denied.Http.GetAsync(photoUrl);Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using var foreignResponse=await foreign.Http.GetAsync(photoUrl);Assert.NotEqual(HttpStatusCode.OK,foreignResponse.StatusCode);
        using var wrongItem=await employee.Http.GetAsync($"{url}/2147483647/photo");Assert.Equal(HttpStatusCode.NotFound,wrongItem.StatusCode);
        using var invalid=await employee.Http.PostAsJsonAsync(url,new {commandId=Guid.NewGuid(),documentRowVersion=state.GetProperty("documentRowVersion").GetString(),
            name="Không được tạo",barcode="INVALID-PHOTO",baseUnitId=unitId,unitId,factor=1,quantity=5,categoryId,photoDataUrl="data:image/svg+xml;base64,PHN2Zz4="});
        Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
        await using var verify=app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await verify.StockDocumentProvisionalItems.Where(x=>x.StockDocumentId==documentId).ToListAsync());
        Assert.False(await verify.Products.AnyAsync(x=>x.Name=="Hàng có ảnh"));
        Assert.Equal(100,(await verify.InventoryBalances.SingleAsync()).OnHandQty);
    }
}

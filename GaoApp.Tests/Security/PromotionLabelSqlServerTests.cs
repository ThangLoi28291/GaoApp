using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Printing;
using GaoApp.Web.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
[SupportedOSPlatform("windows")]
public sealed class PromotionLabelSqlServerTests
{
    private const string Url = "/admin/promotion-labels/";

    [Fact]
    public async Task Manager_only_client_labels_are_tenant_scoped_fresh_and_never_enqueue_server_jobs()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        int campaignId, unitId, foreignUnitId, productId, menuCount;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x=>x.Product).SingleAsync(x=>x.Id==store.VariantId);
            productId=variant.ProductId;
            var conversion = new ProductUnitConversion {StoreId=store.StoreId,ProductVariantId=variant.Id,UnitId=variant.Product.BaseUnitId,
                Factor=1,IsBaseUnit=true,IsActive=true,Price=37800};
            db.Add(conversion);await db.SaveChangesAsync();unitId=conversion.Id;
            db.Add(new ProductVariantUnitBarcode {StoreId=store.StoreId,ProductUnitConversionId=unitId,Barcode="4902430679640",IsPrimary=true,IsActive=true});
            var campaign=Campaign(store.StoreId,productId,"Shelf 15%",PromotionType.ProductDiscount);
            campaign.Items.First().MinQuantity=2;
            db.Add(campaign);
            foreach(var type in new[]{PromotionType.ComboFixedPrice,PromotionType.BuyXGetY})db.Add(Campaign(store.StoreId,productId,type.ToString(),type));
            var expired=Campaign(store.StoreId,productId,"Expired",PromotionType.ProductDiscount);expired.EndAtUtc=DateTime.UtcNow.AddDays(-1);expired.StartAtUtc=DateTime.UtcNow.AddDays(-2);db.Add(expired);
            var locked=Campaign(store.StoreId,productId,"Locked",PromotionType.ProductDiscount);locked.IsActive=false;db.Add(locked);
            var wholesale=Campaign(store.StoreId,productId,"Wholesale",PromotionType.ProductDiscount);wholesale.CustomerPriceTier="WHOLESALE";db.Add(wholesale);
            await db.SaveChangesAsync();campaignId=campaign.Id;
            menuCount=await db.AdminMenuItems.CountAsync();
        }
        var foreign=app.Stores[1];
        await using(var db=app.Database.CreateTenantContext(foreign.StoreId))
        {
            var variant=await db.ProductVariants.Include(x=>x.Product).SingleAsync(x=>x.Id==foreign.VariantId);
            var unit=new ProductUnitConversion {StoreId=foreign.StoreId,ProductVariantId=variant.Id,UnitId=variant.Product.BaseUnitId,IsActive=true,Factor=1,Price=37800};
            db.Add(unit);await db.SaveChangesAsync();foreignUnitId=unit.Id;
            db.Add(Campaign(foreign.StoreId,variant.ProductId,"Foreign",PromotionType.ProductDiscount));await db.SaveChangesAsync();
        }
        using var admin=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using var staff=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.System.ProductLabel.Print));
        using var partial=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Catalog.Promotion.Manage));
        using var other=await app.LoginAsync(await app.AddAccountAsync(foreign,"*"));
        var a4Preview=new PromotionA4Preview(new(PromotionA4Layout.DefaultDesign(),new("Xem mẫu", "",300,210,0,0),
            [new(ProductLabelRenderer.SampleFor(PromotionA4Layout.DefaultDesign()),5)]));
        foreach(var client in new[]{staff,partial})
        {
            foreach(var path in new[]{"","campaigns","client-download","products?promotionId="+campaignId})
            { using var response=await client.Http.GetAsync(Url+path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
            using var post=await client.Http.PostAsJsonAsync(Url+"prepare",new PreparePromotionLabels(campaignId,[new(unitId,1,"fake")]));
            Assert.Equal(HttpStatusCode.Forbidden,post.StatusCode);
            using var previewDenied=await client.Http.PostAsJsonAsync(Url+"preview-a4",a4Preview);
            Assert.Equal(HttpStatusCode.Forbidden,previewDenied.StatusCode);
        }
        using(var html=await admin.Http.GetAsync(Url))
        { Assert.Equal(HttpStatusCode.OK,html.StatusCode);var content=await html.Content.ReadAsStringAsync();Assert.Contains("plPreview",content);Assert.Contains("GaoApp Label Print Client",content);Assert.DoesNotContain("qz-tray.js",content);Assert.DoesNotContain("pos.printing.js",content); }
        using(var a4Image=await admin.Http.PostAsJsonAsync(Url+"preview-a4",a4Preview))
        {
            Assert.Equal(HttpStatusCode.OK,a4Image.StatusCode);Assert.Equal("image/png",a4Image.Content.Headers.ContentType!.MediaType);
            var evidence=Path.GetFullPath(Path.Combine("TestResults","promotion-a4"));Directory.CreateDirectory(evidence);
            await File.WriteAllBytesAsync(Path.Combine(evidence,"server-preview-5.png"),await a4Image.Content.ReadAsByteArrayAsync());
            await File.WriteAllTextAsync(Path.Combine(evidence,"manager-page.html"),await admin.Http.GetStringAsync(Url));
        }
        var campaigns=await admin.JsonAsync(HttpMethod.Get,Url+"campaigns");
        Assert.Equal(campaignId,Assert.Single(campaigns.EnumerateArray()).GetProperty("id").GetInt32());
        using(var response=await other.Http.GetAsync(Url+"products?promotionId="+campaignId))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);

        async Task<PromotionLabelOption> Option() => (await admin.JsonAsync(HttpMethod.Get,Url+$"products?promotionId={campaignId}&pageSize=1"))
            .GetProperty("items")[0].Deserialize<PromotionLabelOption>(LabelJson.Options)!;
        var option=await Option();Assert.Equal(unitId,option.ConversionId);Assert.Equal(32100,option.SalePrice);Assert.Equal(2,option.MinimumQuantity);Assert.Null(option.Problem);
        var found=await admin.JsonAsync(HttpMethod.Get,Url+$"products?promotionId={campaignId}&q=243067");Assert.Equal(1,found.GetProperty("total").GetInt32());
        var empty=await admin.JsonAsync(HttpMethod.Get,Url+$"products?promotionId={campaignId}&q=missing");Assert.Equal(0,empty.GetProperty("total").GetInt32());
        var request=new PreparePromotionLabels(campaignId,[new(unitId,3,option.Fingerprint)]);
        var prepared=await admin.JsonAsync(HttpMethod.Post,Url+"prepare",request);
        Assert.Equal(3,prepared[0].GetProperty("quantity").GetInt32());Assert.Equal(32100,prepared[0].GetProperty("product").GetProperty("salePrice").GetDecimal());
        using(var invalid=await admin.Http.PostAsJsonAsync(Url+"prepare",request with {Lines=[new(unitId,1001,option.Fingerprint)]}))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var cross=await admin.Http.PostAsJsonAsync(Url+"prepare",request with {Lines=[new(foreignUnitId,1,option.Fingerprint)]}))Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
        using(var fake=await admin.Http.PostAsJsonAsync(Url+"prepare",request with {Lines=[new(unitId,1,"fake")]}))Assert.Equal(HttpStatusCode.Conflict,fake.StatusCode);
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        { (await db.ProductUnitConversions.SingleAsync(x=>x.Id==unitId)).Price=40000;await db.SaveChangesAsync(); }
        using(var stale=await admin.Http.PostAsJsonAsync(Url+"prepare",request))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var refreshed=await admin.JsonAsync(HttpMethod.Post,Url+"refresh",new RefreshPromotionLabels(campaignId,[unitId]));
        Assert.Equal(34000,refreshed[0].GetProperty("salePrice").GetDecimal());
        option=await Option();request=request with {Lines=[new(unitId,2,option.Fingerprint)]};
        await admin.JsonAsync(HttpMethod.Post,Url+"prepare",request);
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        { var higher=Campaign(store.StoreId,productId,"Higher priority",PromotionType.ProductDiscount);higher.Priority=100;db.Add(higher);await db.SaveChangesAsync(); }
        Assert.Contains("Higher priority",(await Option()).Problem);
        using(var priority=await admin.Http.PostAsJsonAsync(Url+"prepare",request))Assert.Equal(HttpStatusCode.Conflict,priority.StatusCode);
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Empty(await db.Set<ProductLabelJob>().ToListAsync());
            Assert.Equal(menuCount,await db.AdminMenuItems.CountAsync());
            (await db.Promotions.SingleAsync(x=>x.Id==campaignId)).IsActive=false;await db.SaveChangesAsync();
        }
        using(var locked=await admin.Http.PostAsJsonAsync(Url+"prepare",request))Assert.Equal(HttpStatusCode.NotFound,locked.StatusCode);
    }

    private static Promotion Campaign(int storeId,int productId,string name,PromotionType type) => new()
    {
        StoreId=storeId,Name=name,Type=type,IsActive=true,StartAtUtc=DateTime.UtcNow.AddDays(-1),EndAtUtc=DateTime.UtcNow.AddDays(1),
        DiscountType=PromotionDiscountType.Percentage,DiscountValue=15,Priority=1,
        Items=[new() {StoreId=storeId,ProductId=productId,MinQuantity=1}]
    };
}

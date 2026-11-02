using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Razor.Hosting;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
[SupportedOSPlatform("windows")]
public sealed class PromotionsMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Native_promotion_display_and_label_phone_views_preserve_scope_rights_drafts_and_desktop()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, "*");
        var reader = await app.AddAccountAsync(store, PermissionCodes.Catalog.Promotion.View, PermissionCodes.Catalog.DisplayPromotion.View);
        var denied = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var partial = await app.AddAccountAsync(store, PermissionCodes.Catalog.Promotion.Manage);
        using var admin = await app.LoginAsync(manager);
        using var view = await app.LoginAsync(reader);
        using var no = await app.LoginAsync(denied);
        using var incomplete = await app.LoginAsync(partial);
        using var other = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var ids = new Dictionary<string, int>();
        int conversionId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var variant = await db.ProductVariants.Include(x => x.Product).Include(x => x.UnitConversions).SingleAsync(x => x.Id == store.VariantId);
            variant.Product.Name = "Sữa tươi và ngũ cốc nguyên hạt · Sản phẩm kiểm thử có tên dài để đọc trên điện thoại";
            variant.Price = 40000;
            var conversion = variant.UnitConversions.FirstOrDefault(x => x.IsBaseUnit);
            if (conversion is null) {
                conversion = new ProductUnitConversion { StoreId=store.StoreId, ProductVariantId=variant.Id,
                    UnitId=variant.Product.BaseUnitId, Factor=1, IsBaseUnit=true, IsDefaultForSale=true, IsActive=true };
                db.Add(conversion);
            }
            conversion.Price=40000;
            await db.SaveChangesAsync(); conversionId=conversion.Id;
            db.Add(new ProductVariantUnitBarcode { StoreId=store.StoreId, ProductUnitConversionId=conversion.Id, Barcode="4902430679640", IsPrimary=true, IsActive=true });
            Promotion Campaign(string key, PromotionType type = PromotionType.ProductDiscount) => new() {
                StoreId=store.StoreId, Name="UI19 "+key+" · Ưu đãi cửa hàng dành cho khách mua hàng có tên dài",
                Description="Điều kiện và quyền lợi từ dữ liệu thật trong cơ sở dữ liệu kiểm thử riêng.", Type=type,
                DiscountType=PromotionDiscountType.Percentage, DiscountValue=15, IsActive=true, Priority=1,
                StartAtUtc=DateTime.UtcNow.AddDays(-2), EndAtUtc=DateTime.UtcNow.AddDays(3),
                Items=type==PromotionType.ComboFixedPrice?[]:[new() { StoreId=store.StoreId, ProductId=variant.ProductId, MinQuantity=2 }]
            };
            var campaigns = new Dictionary<string, Promotion> { ["current"]=Campaign("current"), ["expired"]=Campaign("expired"),
                ["future"]=Campaign("future"), ["inactive"]=Campaign("inactive"), ["combo"]=Campaign("combo",PromotionType.ComboFixedPrice),
                ["mixed"]=Campaign("mixed",PromotionType.ComboFixedPrice), ["gift"]=Campaign("gift",PromotionType.BuyXGetY) };
            campaigns["expired"].StartAtUtc=DateTime.UtcNow.AddDays(-5); campaigns["expired"].EndAtUtc=DateTime.UtcNow.AddDays(-1);
            campaigns["future"].StartAtUtc=DateTime.UtcNow.AddDays(2); campaigns["future"].EndAtUtc=DateTime.UtcNow.AddDays(5);
            campaigns["inactive"].IsActive=false;
            campaigns["combo"].ComboFixedPrice=65000;
            campaigns["combo"].ComboRules=[new() { StoreId=store.StoreId, ProductId=variant.ProductId, VariantId=variant.Id, RequiredQuantity=2 }];
            campaigns["mixed"].ComboFixedPrice=180000; campaigns["mixed"].ComboQuantity=6; campaigns["mixed"].ComboBaseUnitId=variant.Product.BaseUnitId;
            campaigns["mixed"].ComboPricingMode=ComboPricingMode.MixedQuantity;
            campaigns["mixed"].ComboRules=[new() { StoreId=store.StoreId, ProductId=variant.ProductId, VariantId=variant.Id, RequiredQuantity=1 }];
            campaigns["gift"].BuyQuantity=10; campaigns["gift"].GetQuantity=1; campaigns["gift"].RequireGiftQuantityInCart=true;
            db.AddRange(campaigns.Values); await db.SaveChangesAsync();
            foreach(var pair in campaigns) ids[pair.Key]=pair.Value.Id;
            foreach(var key in new[] { "text", "image", "hidden", "expired" }) {
                var content = new DisplayPromotion { StoreId=store.StoreId, Title="UI19 "+key+" · Lời chào và thông tin ưu đãi dành cho khách hàng tại quầy",
                    Description="Nội dung, lịch phát và thứ tự giữ đúng dữ liệu nguồn.", MediaType=key=="image"?"image":"text",
                    MediaUrl=key=="image"?"/Admin/img/customer-display/grocery-bag.svg":null, DurationSeconds=8, SortOrder=ids.Count,
                    BackgroundColor="#12324b", TextColor="#ffffff", StartAt=DateTime.Now.AddDays(-2), EndAt=DateTime.Now.AddDays(key=="expired"?-1:2), IsActive=key!="hidden" };
                db.Add(content); await db.SaveChangesAsync(); ids["display-"+key]=content.Id;
            }
        }
        int foreignPromotion, foreignDisplay;
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId)) {
            var campaign=new Promotion { StoreId=app.Stores[1].StoreId, Name="UI19_FOREIGN_PRIVATE", Type=PromotionType.ProductDiscount,
                DiscountType=PromotionDiscountType.Percentage, DiscountValue=10, IsActive=true, StartAtUtc=DateTime.UtcNow.AddDays(-1), EndAtUtc=DateTime.UtcNow.AddDays(2) };
            var content=new DisplayPromotion { StoreId=app.Stores[1].StoreId, Title="UI19_FOREIGN_PRIVATE", MediaType="text", IsActive=true };
            db.AddRange(campaign,content); await db.SaveChangesAsync(); foreignPromotion=campaign.Id; foreignDisplay=content.Id;
        }
        var forbiddenChecks=new List<object>();
        foreach(var route in new[] { "/admin/promotion", "/admin/displaypromotion", "/admin/promotion-labels", "/admin/promotion-labels/campaigns" }) {
            using var response=await no.Http.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            forbiddenChecks.Add(new { method="GET", route, status=403 });
        }
        foreach(var client in new[] {view,incomplete}) {
            using var response=await client.Http.GetAsync("/admin/promotion-labels"); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        foreach(var route in new[] { "/admin/promotion/save", "/admin/promotion/togglestatus?id="+ids["current"],
            "/admin/promotion/deleteajax?id="+ids["current"], "/admin/promotion/duplicate?id="+ids["current"],
            "/admin/displaypromotion/create", "/admin/displaypromotion/update?id="+ids["display-text"], "/admin/displaypromotion/delete?id="+ids["display-text"],
            "/admin/displaypromotion/toggleactive?id="+ids["display-text"], "/admin/promotion-labels/prepare" }) {
            using var response=await view.Http.PostAsJsonAsync(route,new { }); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            forbiddenChecks.Add(new { method="POST", route, status=403 });
        }
        using(var response=await view.Http.PutAsJsonAsync("/admin/displaypromotion/savewifi",new { name="forbidden" })) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.False((await admin.JsonAsync(HttpMethod.Get,"/admin/promotion/get?id="+foreignPromotion)).GetProperty("success").GetBoolean());
        Assert.False((await admin.JsonAsync(HttpMethod.Get,"/admin/displaypromotion/detail?id="+foreignDisplay)).GetProperty("success").GetBoolean());
        using(var response=await other.Http.GetAsync("/admin/promotion-labels/products?promotionId="+ids["current"])) Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        var active=await admin.JsonAsync(HttpMethod.Get,"/admin/displaypromotion/activeforcustomerdisplay");
        var activeIds=active.GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(ids["display-text"],activeIds); Assert.Contains(ids["display-image"],activeIds);
        Assert.DoesNotContain(ids["display-hidden"],activeIds); Assert.DoesNotContain(ids["display-expired"],activeIds); Assert.DoesNotContain(foreignDisplay,activeIds);
        var labelCampaigns=await admin.JsonAsync(HttpMethod.Get,"/admin/promotion-labels/campaigns");
        Assert.Equal(new[] { ids["current"],ids["future"] }.Order(),labelCampaigns.EnumerateArray().Select(x=>x.GetProperty("id").GetInt32()).Order());
        var product=await admin.JsonAsync(HttpMethod.Get,"/admin/promotion-labels/products?promotionId="+ids["current"]);
        var option=Assert.Single(product.GetProperty("items").EnumerateArray()); Assert.Equal(34000,option.GetProperty("salePrice").GetDecimal());
        Assert.Equal(2,option.GetProperty("minimumQuantity").GetDecimal());
        var root=FullApplicationFixture.SourceRoot(); var output=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-19-20261027","browser"); Directory.CreateDirectory(output);
        var webAssembly=typeof(GaoApp.Web.Areas.Admin.Controllers.PromotionController).Assembly;
        var compiledViews=new RazorCompiledItemLoader().LoadItems(webAssembly).Select(item=>item.Identifier).Order().ToArray();
        var expectedViews=new[] {"Promotion/Index","DisplayPromotion/Index","PromotionLabels/Index","POS/Index","CustomerChannels/Index", "Invoice/Detail","Invoice/_CreateManualDetailModal","Invoice/_InvoiceAutoLinesTable","Invoice/_InvoiceDetailHeader","Invoice/_InvoiceManualModals","Invoice/_ManualLinesTable"}
            .Select(view=>"/Areas/Admin/Views/"+view+".cshtml").ToArray();
        foreach(var expected in expectedViews) Assert.Contains(expected,compiledViews);
        await File.WriteAllTextAsync(Path.Combine(output,"compiled-views.json"),JsonSerializer.Serialize(new {passed=true,expectedViews,compiledViews,
            assemblyPath=webAssembly.Location,assemblySha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(webAssembly.Location))).ToLowerInvariant()},new JsonSerializerOptions {WriteIndented=true}));
        var expectedAssetsPath=Environment.GetEnvironmentVariable("GAOAPP_UI19_EXPECTED_ASSETS");
        if(!string.IsNullOrWhiteSpace(expectedAssetsPath)) {
            var expectedAssets=JsonDocument.Parse(await File.ReadAllTextAsync(expectedAssetsPath)).RootElement.GetProperty("assets");
            var assetChecks=new List<object>();
            foreach(var expected in expectedAssets.EnumerateArray()) {
                var url=expected.GetProperty("url").GetString()!; var expectedSha=expected.GetProperty("sha256").GetString()!;
                foreach(var encoding in new[] { "identity","gzip" }) {
                    using var request=new HttpRequestMessage(HttpMethod.Get,url); request.Headers.AcceptEncoding.ParseAdd(encoding);
                    using var response=await admin.Http.SendAsync(request); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                    var bytes=await response.Content.ReadAsByteArrayAsync();
                    var actualEncoding=response.Content.Headers.ContentEncoding.ToArray();
                    Assert.True(actualEncoding.Length==0||actualEncoding.SequenceEqual(new[] {"gzip"}),"Only native identity/gzip transport is expected.");
                    if(actualEncoding.Contains("gzip")) {
                        using var compressed=new MemoryStream(bytes); using var decoder=new GZipStream(compressed,CompressionMode.Decompress);
                        using var decoded=new MemoryStream(); await decoder.CopyToAsync(decoded); bytes=decoded.ToArray();
                    }
                    if(encoding=="identity") Assert.Empty(actualEncoding);
                    var actualSha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); Assert.Equal(expectedSha,actualSha);
                    assetChecks.Add(new {url,requestedEncoding=encoding,actualEncoding,expectedSha256=expectedSha,actualSha256=actualSha,decodedBytes=bytes.Length});
                }
            }
            await File.WriteAllTextAsync(Path.Combine(output,"served-assets.json"),JsonSerializer.Serialize(new {passed=true,manifest=expectedAssetsPath,checks=assetChecks},new JsonSerializerOptions {WriteIndented=true}));
        }
        var previewDesign=new ProductLabelDesign { Layout="promotion-shelf", WidthMm=50, HeightMm=30, Columns=1 };
        await File.WriteAllBytesAsync(Path.Combine(output,"native-label-preview.png"),ProductLabelRenderer.Preview(previewDesign,ProductLabelRenderer.SampleFor(previewDesign),203));
        var before=await StateAsync(app);
        var start=new ProcessStartInfo("node") { WorkingDirectory=root, UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true };
        start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","promotions-mobile.browser.cjs"));
        using var process=Process.Start(start)!; var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl=$"http://{store.Host}:{app.Address.Port}", terminalId=store.TerminalId, output,
            manager=new { user=manager.Name,password=manager.Password },reader=new { user=reader.Name,password=reader.Password }, ids, conversionId, expectedLabelPrice=34000, expectedMinimumQuantity=2 }));
        process.StandardInput.Close(); using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if(!process.HasExited)process.Kill(true); throw; }
        var log=await stdout+"\n"+await stderr; await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log); Assert.True(process.ExitCode==0,log);
        var after=await StateAsync(app); Assert.Equal(before,after);
        var state=JsonDocument.Parse(after).RootElement;
        var stateCounts=state.EnumerateObject().ToDictionary(property=>property.Name,property=>property.Value.GetArrayLength());
        var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"promotions-mobile-result.json"))).RootElement;
        Assert.True(result.GetProperty("passed").GetBoolean()); Assert.True(result.GetProperty("mandatoryMatrixExecuted").GetBoolean());
        Assert.True(result.GetProperty("nativeStateCount").GetInt32()>=20);
        Assert.Equal(0,result.GetProperty("unexpectedBusinessWrites").GetArrayLength()); Assert.Equal(0,result.GetProperty("pageErrors").GetArrayLength());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new { passed=true, fixtureOnly=true,
            nativeStoreRowsCampaignsRulesDisplaySchedulesProductsPricesStockAndMenuConfigurationUnchanged=true,
            beforeSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(before))).ToLowerInvariant(),
            afterSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(after))).ToLowerInvariant(), stateCounts,
            readOnlyCommandsServerForbidden=forbiddenChecks, foreignRecordsExcluded=true, activeDisplayScheduleRespected=true,
            eligibleLabelCampaignsOnly=true, actualLabelPrice=34000, actualMinimumQuantity=2, noPrinterJobSubmitted=true },new JsonSerializerOptions { WriteIndented=true }));
    }

    private static async Task<string> StateAsync(FullApplicationFixture app)
    {
        await using var db=app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>() where TEntity:BaseEntity {
            var rows=await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).ToListAsync();
            return rows.Select(row=>db.Entry(row).Properties.OrderBy(x=>x.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new { stores=await Rows<Store>(), menus=await Rows<AdminMenuItem>(), visibility=await Rows<AdminMenuVisibilitySetting>(), pins=await Rows<AdminMenuPinSetting>(),
            roles=await Rows<Role>(), grants=await db.RolePermissions.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoleId,x.PermissionId}).ToListAsync(),
            campaigns=await Rows<Promotion>(), items=await Rows<PromotionItem>(), rules=await Rows<PromotionComboRule>(), display=await Rows<DisplayPromotion>(),
            products=await Rows<Product>(), variants=await Rows<ProductVariant>(), conversions=await Rows<ProductUnitConversion>(), stock=await Rows<InventoryBalance>(),
            jobs=await Rows<ProductLabelJob>(), templates=await Rows<ProductLabelTemplate>(), printers=await Rows<ProductLabelPrinter>() });
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed partial class AssistantIntegrationTests
{
    [Fact]
    public async Task Barcode_search_matches_only_current_active_exact_codes_in_the_store()
    {
        using var f = new Fixture();await f.Seed();
        var conversion = await f.Db.ProductUnitConversions.SingleAsync(c => c.Id == 102);
        f.Db.ProductVariantUnitBarcodes.AddRange(
            new ProductVariantUnitBarcode { Id = 901, StoreId = 1, ProductUnitConversionId = conversion.Id, ProductUnitConversion = conversion, Barcode = "0008935217400157", IsActive = true, RowVersion = [1] },
            new ProductVariantUnitBarcode { Id = 902, StoreId = 1, ProductUnitConversionId = conversion.Id, ProductUnitConversion = conversion, Barcode = "ABC-001", IsActive = true, RowVersion = [1] },
            new ProductVariantUnitBarcode { Id = 903, StoreId = 1, ProductUnitConversionId = conversion.Id, ProductUnitConversion = conversion, Barcode = "OLD-001", IsActive = false, RowVersion = [1] });
        await f.Db.SaveChangesAsync();
        var service = new AssistantCatalogService(f.Db, f.Clock);
        var who = await f.Access.CurrentAsync(null, default);
        foreach (var code in new[] { "0008935217400157", "ABC-001" })
        {
            var page = await service.SearchAsync(who, new CatalogSearch { Query = "barcode:" + code, Limit = 2 }, default);
            Assert.Equal("10", Assert.Single(page.Variants).Product.VariantId);Assert.Null(page.Cursor);
        }
        foreach (var code in new[] { "8935217400157", "ABC-00", "OLD-001", "TH180-1" })
            Assert.Empty((await service.SearchAsync(who, new CatalogSearch { Query = "barcode:" + code, Limit = 2 }, default)).Variants);
        Assert.Empty((await service.SearchAsync(who with { Scope = new("2", null, null) }, new CatalogSearch { Query = "barcode:ABC-001", Limit = 2 }, default)).Variants);
        conversion.IsActive = false;await f.Db.SaveChangesAsync();
        Assert.Empty((await service.SearchAsync(who, new CatalogSearch { Query = "barcode:ABC-001", Limit = 2 }, default)).Variants);
    }
    [Fact]
    public async Task Launcher_failure_returns_empty_content_and_does_not_break_the_host_page()
    {
        using var f = new Fixture();
        await f.Db.DisposeAsync();
        var component = new GaoApp.Web.ViewComponents.AssistantViewComponent(f.Access, f.Options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GaoApp.Web.ViewComponents.AssistantViewComponent>.Instance);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([], "Test"));
        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = context }
        };
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewComponents.ContentViewComponentResult>(await component.InvokeAsync());
        Assert.Equal("", result.Content);
    }

    [Fact]
    public void Jwt_pair_has_distinct_types_and_audiences_rejects_context_tamper_and_expiry()
    {
        using var fixture = new Fixture();
        var who = new AssistantIdentity(1, "Nguyễn Lan", new("1", "2", "3"), AssistantPermissions.All);
        var pair = fixture.Jwt.Sign(who);
        Assert.Equal(who.Scope, fixture.Jwt.ValidateDelegation(pair.Delegation).Scope);
        Assert.Equal(who.DisplayName, fixture.Jwt.ValidateDelegation(pair.Delegation).DisplayName);
        Assert.Equal(401, Assert.Throws<AssistantException>(()=>fixture.Jwt.ValidateDelegation(pair.Context)).Status);
        var pieces = pair.Delegation.Split('.');
        pieces[1] = pieces[1][..^2] + "AA";
        Assert.Equal(401, Assert.Throws<AssistantException>(()=>fixture.Jwt.ValidateDelegation(string.Join('.',pieces))).Status);
        fixture.Clock.Now += TimeSpan.FromSeconds(136);
        Assert.Equal(401, Assert.Throws<AssistantException>(()=>fixture.Jwt.ValidateDelegation(pair.Delegation)).Status);
        fixture.Options.CurrentValue.Enabled = false;
        Assert.Equal(503, Assert.Throws<AssistantException>(()=>fixture.Jwt.Sign(who)).Status);
    }

    [Fact]
    public void Strict_wire_rejects_duplicate_unknown_fields_and_requires_nullable_scope_fields()
    {
        using var duplicate=JsonDocument.Parse("{\"query\":\"sữa\",\"query\":\"TH\"}");
        Assert.Equal(400,Assert.Throws<AssistantException>(()=>AssistantJwt.Unique(duplicate.RootElement)).Status);
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<QuoteLookup>("{\"costPrice\":1}",json));
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<AssistantScope>("{\"storeId\":\"1\"}",json));
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<QuoteLookup>("{\"items\":[{\"productId\":1,\"variantId\":\"2\",\"unitId\":\"3\"}]}",json));
    }

    [Fact]
    public async Task Callback_intersects_current_permissions_and_revocation_denies_access()
    {
        using var f=new Fixture();await f.Seed();
        var signed=new AssistantIdentity(2,"Lan",new("1",null,null),AssistantPermissions.All);
        var current=await f.Access.RecheckAsync(signed,default);
        Assert.Contains(AssistantPermissions.Use,current.Permissions);
        Assert.Contains(AssistantPermissions.Catalog,current.Permissions);
        Assert.DoesNotContain(AssistantPermissions.Wholesale,current.Permissions);
        Assert.DoesNotContain(AssistantPermissions.Receiving,current.Permissions);
        var old=current.Permissions;
        var role=await f.Db.Roles.SingleAsync();f.Db.RolePermissions.RemoveRange(role.RolePermissions.ToArray());await f.Db.SaveChangesAsync();
        current=await f.Access.RecheckAsync(signed,default);
        Assert.Empty(current.Permissions);
        Assert.Equal(403,Assert.Throws<AssistantException>(()=>AssistantAccess.Require(current.Permissions,AssistantPermissions.Use)).Status);
        Assert.NotEmpty(old);
    }

    [Fact]
    public async Task Identity_and_catalog_are_scoped_even_when_host_tenant_differs()
    {
        using var f=new Fixture();await f.Seed();
        var who=await f.Access.CurrentAsync(null,default);
        Assert.Equal("Tên nhân viên",who.DisplayName);
        Assert.Equal("1",who.Scope.StoreId);
        Assert.Equal("Store1", await f.Access.CurrentStoreNameAsync(who, default));
        Assert.Equal(403, (await Assert.ThrowsAsync<AssistantException>(() =>
            f.Access.CurrentStoreNameAsync(who with { Scope = new("2", null, null) }, default))).Status);
        var catalog=new AssistantCatalogService(f.Db,f.Clock);
        Assert.Equal(10,(await catalog.FindAsync(who,"10",default)).Id);
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>catalog.FindAsync(who,"20",default))).Status);
        var second=who with {Scope=new("2",null,null)};
        Assert.Equal(20,(await catalog.FindAsync(second,"20",default)).Id);
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ValidateScopeAsync(new("1","2",null),default))).Status);
    }

    [Fact]
    public void Catalog_preserves_all_units_and_decimals_omits_wholesale_cost_and_inventory()
    {
        var variant=Variant(10,1);var catalog=AssistantCatalogService.Build(variant,true);
        Assert.Equal(23,catalog.Units.Length);
        Assert.Single(catalog.Units,u=>u.Unit.IsBaseUnit);
        Assert.Equal("4",catalog.Units[1].Unit.Factor);
        Assert.Equal("180ml",catalog.Attributes.Capacity);
        Assert.Equal("sweetened",catalog.Attributes.Sugar);
        var json=JsonSerializer.Serialize(catalog,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("wholesale",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stock",json,StringComparison.OrdinalIgnoreCase);
        Assert.All(AssistantCatalogService.Build(variant,false).Units,u=>{Assert.Equal("missing",u.Retail.Status);Assert.Null(u.Retail.Amount);});
        Assert.Equal("0.75",AssistantCatalogService.Decimal(.75m));
        variant.Product.IsSellable=false;
        Assert.All(AssistantCatalogService.Build(variant,true).Units,u=>{Assert.True(u.Unit.CanReceive);Assert.False(u.Unit.CanSell);});
    }

    [Fact]
    public void Prices_use_stored_unit_override_zero_and_missing_only_fallback()
    {
        var v=Variant(10,1);var unit=v.UnitConversions.Skip(1).First();
        unit.Price=36000;unit.WholesalePrice=34000;
        Assert.Equal("36000",AssistantCatalogService.Price(v,unit,"retail").Amount);
        Assert.Equal("34000",AssistantCatalogService.Price(v,unit,"wholesale").Amount);
        unit.WholesalePrice=null;var fallback=AssistantCatalogService.Price(v,unit,"wholesale");
        Assert.Equal("retail",fallback.AppliedTier);Assert.Equal("wholesale_missing",fallback.FallbackReason);
        unit.WholesalePrice=0;Assert.Equal("0",AssistantCatalogService.Price(v,unit,"wholesale").Amount);
        unit.WholesalePrice=-1;var error=AssistantCatalogService.Price(v,unit,"wholesale");
        Assert.Equal("error",error.Status);Assert.Null(error.FallbackReason);Assert.Null(error.Amount);
        unit.Price=-1;Assert.Equal("error",AssistantCatalogService.Price(v,unit,"retail").Status);
    }

    [Theory]
    [InlineData("Sữa tươi TH nguyên chất hộp 180ml", "unsweetened")]
    [InlineData("Sữa tươi TH nguyên chất 110ml", "unsweetened")]
    [InlineData("Sữa tươi TH nguyên chất bịch 220ml", "unsweetened")]
    [InlineData("Sữa tươi TH nguyên chất hộp 1L", "unsweetened")]
    [InlineData("Sữa tươi TH có đường nguyên chất 180ml", "sweetened")]
    [InlineData("Sữa tươi TH chocolate nguyên chất 180ml", null)]
    [InlineData("Sữa tươi NhãnMới nguyên chất 180ml", null)]
    public void Confirmed_plain_TH_alias_does_not_classify_other_pure_or_flavoured_products(string name, string? sugar)
    {
        var variant=Variant(10,1);variant.Product.Name=name;variant.ProductVariantName=name;
        Assert.Equal(sugar,AssistantCatalogService.Build(variant,true).Attributes.Sugar);
    }

    [Fact]
    public async Task Search_unsweetened_TH_alias_returns_fresh_source_units_and_excludes_sweetened()
    {
        using var f=new Fixture();await f.Seed();
        var original=await f.Db.ProductVariants.Include(v=>v.Product).SingleAsync(v=>v.Id==10);
        original.Product.Name="Sữa tươi TH nguyên chất hộp 180ml";original.ProductVariantName=original.Product.Name;
        original.ProductVariantNameNormalized="sua tuoi th nguyen chat hop 180ml";
        await f.Db.SaveChangesAsync();
        var who=await f.Access.CurrentAsync(null,default);var service=new AssistantCatalogService(f.Db,f.Clock);
        var page=await service.SearchAsync(who,new CatalogSearch{Query="sữa tươi TH không đường 180ml",Limit=20},default);
        var item=Assert.Single(page.Variants);Assert.Equal("10",item.Product.VariantId);
        Assert.Equal("unsweetened",item.Attributes.Sugar);Assert.NotEmpty(item.Units);
        Assert.Empty((await service.SearchAsync(who,new CatalogSearch{Query="sữa TH có đường 180ml",Limit=20},default)).Variants);
    }

    [Fact]
    public async Task Quote_checks_tier_rights_exact_product_unit_conversion_and_batch_limit()
    {
        using var f=new Fixture();await f.Seed();var c=new AssistantCatalogService(f.Db,f.Clock);
        var who=await f.Access.CurrentAsync(null,default);
        var request=new QuoteLookup {Items=[new("10","10","1002","102")],Tiers=["retail"]};
        var result=await c.QuoteAsync(who,request,default);Assert.Equal("1002",result.Single().Unit.UnitId);
        request.Tiers=["wholesale"];
        var employee=await f.Access.RecheckAsync(new(2,"Lan",who.Scope,AssistantPermissions.All),default);
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>c.QuoteAsync(employee,request,default))).Status);
        request.Items=[new("20","10","1002","102")];request.Tiers=["retail"];
        Assert.Equal(422,(await Assert.ThrowsAsync<AssistantException>(()=>c.QuoteAsync(who,request,default))).Status);
        request.Items=[new("10","10","1002","103")];
        Assert.Equal(422,(await Assert.ThrowsAsync<AssistantException>(()=>c.QuoteAsync(who,request,default))).Status);
        request.Items=Enumerable.Range(1,21).Select(i=>new QuoteItem("10","10",i.ToString(),(100+i).ToString())).ToArray();
        Assert.Equal(400,(await Assert.ThrowsAsync<AssistantException>(()=>c.QuoteAsync(who,request,default))).Status);
    }

    [Fact]
    public async Task Store_switches_persist_isolate_stores_and_revoke_host_permissions()
    {
        using var f = new Fixture(); await f.Seed();
        var original = await f.Settings.ReadAsync(1, default);
        var off = await f.Settings.SaveAsync(1, 1, original with { ChatEnabled = false }, original.Version, default);
        var rights = await f.Access.PermissionsAsync(1, 1, default);
        Assert.DoesNotContain(AssistantPermissions.Chat, rights);
        Assert.DoesNotContain(AssistantPermissions.Retail, rights);
        Assert.Contains(AssistantPermissions.Receiving, rights);
        Assert.Contains(AssistantPermissions.Chat, await f.Access.PermissionsAsync(1, 2, default));
        Assert.Equal(409, (await Assert.ThrowsAsync<AssistantException>(() => f.Settings.SaveAsync(1, 1, original, original.Version, default))).Status);
        var recreated = new AssistantStoreSettings(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AssistantStoreSettings>.Instance);
        Assert.False((await recreated.ReadAsync(1, default)).ChatEnabled);
        await f.Settings.SaveAsync(1, 1, off with { Enabled = false }, off.Version, default);
        Assert.Empty(await f.Access.PermissionsAsync(1, 1, default));
        Assert.Equal(403, (await Assert.ThrowsAsync<AssistantException>(() => f.Access.CurrentAsync(null, default))).Status);
    }

    [Fact]
    public async Task Unreadable_settings_do_not_enable_AI_and_individual_switches_remove_only_their_permissions()
    {
        using var f = new Fixture(); await f.Seed();
        var initial = await f.Settings.ReadAsync(1, default);
        await f.Settings.SaveAsync(1, 1, initial with { ReceivingEnabled = false, NormalizeEnabled = false }, initial.Version, default);
        var rights = await f.Access.PermissionsAsync(1, 1, default);
        Assert.Contains(AssistantPermissions.Chat, rights);
        Assert.DoesNotContain(AssistantPermissions.Receiving, rights);
        Assert.DoesNotContain(AssistantPermissions.NormalizeName, rights);
        await File.WriteAllTextAsync(Path.Combine(f.DirectoryPath, "App_Data", "Assistant", "store-1.json"), "{invalid}");
        Assert.Equal(503, (await Assert.ThrowsAsync<AssistantException>(() => f.Access.PermissionsAsync(1, 1, default))).Status);
    }

    internal static ProductVariant Variant(int id,int store)
    {
        var baseUnit=new Unit {Id=1+store*1000,StoreId=store,Code="HOP",Name="Hộp",IsActive=true};
        var category=new Category {Id=store*1000+50,StoreId=store,Code="MILK",Name="Sữa",IsActive=true};
        var supplier=new Supplier {Id=store*1000+50,StoreId=store,Code="SUP",Name="TEST"};
        var product=new Product {Id=id,StoreId=store,Name="Sữa tươi TH có đường hộp 180ml",Alias="th180-"+store,CategoryId=category.Id,Category=category,SupplierId=supplier.Id,Supplier=supplier,BaseUnitId=baseUnit.Id,BaseUnit=baseUnit,BasePrice=9000,IsActive=true,IsSellable=true};
        var v=new ProductVariant {Id=id,StoreId=store,ProductId=id,Product=product,Sku="TH180-"+store,Price=9000,CostPrice=4567,IsActive=true};
        for(var i=1;i<=23;i++){
            var u=i==1?baseUnit:new Unit {Id=store*1000+i,StoreId=store,Code="U"+i,Name=i==2?"Lốc":"Đơn vị "+i,IsActive=true};
            v.UnitConversions.Add(new ProductUnitConversion {Id=id*10+i,StoreId=store,ProductVariantId=id,ProductVariant=v,UnitId=u.Id,Unit=u,Factor=i==1?1:i==2?4:i,Price=i==1?9000:36000,IsActive=true,IsBaseUnit=i==1});
        }
        return v;
    }
    private sealed class Fixture : IDisposable
    {
        internal AppDbContext Db {get;}
        internal AssistantAccess Access {get;}
        internal FixedClock Clock {get;}=new();
        internal Monitor<AssistantOptions> Options {get;}
        internal AssistantJwt Jwt {get;}
        internal AssistantStoreSettings Settings {get;}
        internal string DirectoryPath => directory;
        internal void SetStoreScope(int? storeId) => tenant.StoreId = storeId;
        private readonly Tenant tenant=new();
        private readonly string directory=Path.Combine(Path.GetTempPath(),"gao-assistant-"+Guid.NewGuid());
        internal Fixture(){Directory.CreateDirectory(directory);using var rsa=RSA.Create(2048);var path=Path.Combine(directory,"private.pem");File.WriteAllText(path,rsa.ExportRSAPrivateKeyPem());
            Options=new(new(){Enabled=true,Issuer="https://gaoapp.test",KeyId="integration-test",PrivateKeyFile=path});
            Jwt=new(Options,new EnvironmentStub(directory),Clock);var user=new CurrentUser();
            Settings = new(new EnvironmentStub(directory), Options, Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssistantStoreSettings>.Instance);
            Db=new GaoApp.Tests.Data.InMemoryAppDbContext(new DbContextOptionsBuilder<GaoApp.Tests.Data.InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,tenant,user);Access=new(Db,user,tenant,Settings);}
        internal async Task Seed(){
            tenant.StoreId=null;
            Db.Stores.AddRange(new Store {Id=1,Name="Store1",SubDomain="one",SubDomainNormalized="ONE",IsActive=true},new Store{Id=2,Name="Store2",SubDomain="two",SubDomainNormalized="TWO",IsActive=true});
            Db.Users.AddRange(new User{Id=1,UserName="host",FullName="Tên nhân viên",PasswordHash="test",IsHostAdmin=true,IsActive=true},new User{Id=2,UserName="employee",PasswordHash="test",IsActive=true});
            var role=new Role{Id=1,StoreId=1,Code="EMPLOYEE",Name="Employee"};var n=1;
            foreach(var code in new[]{AssistantPermissions.Use,AssistantPermissions.Chat,AssistantPermissions.Retail,PermissionCodes.Catalog.Product.View}){var p=new Permission{Id=n++,Code=code,Name=code,GroupName="TEST"};role.RolePermissions.Add(new RolePermission{RoleId=1,Role=role,PermissionId=p.Id,Permission=p});}
            Db.UserInStores.Add(new UserInStore{Id=1,StoreId=1,UserId=2,RoleId=1,Role=role,IsActive=true});
            Db.ProductVariants.AddRange(Variant(10,1),Variant(20,2));
            foreach(var e in Db.ChangeTracker.Entries<GaoApp.Domain.Common.BaseEntity>())e.Entity.RowVersion=[1];
            await Db.SaveChangesAsync();tenant.StoreId=1;
        }
        public void Dispose(){Db.Dispose();Directory.Delete(directory,true);}
    }
    internal sealed class FixedClock : TimeProvider {internal DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    internal sealed class Monitor<T>(T value):IOptionsMonitor<T> {public T CurrentValue=>value;public T Get(string? name)=>value;public IDisposable? OnChange(Action<T,string?> listener)=>null;}
    // This service fixture represents an explicitly authorized host reader. Cross-tenant middleware/SQL isolation is tested separately.
    // Its null-store phase is used only to provision the two synthetic stores in Seed().
    private sealed class Tenant:ITenantContext {public int? StoreId{get;set;}=1;public bool IsHostAdmin=>true;public string? Subdomain=>"one";public bool CanReadAllStores=>true;public bool CanWriteAllStores=>StoreId is null;}
    private sealed class CurrentUser:ICurrentUser {public int? UserId=>1;public string? UserName=>"host";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
    internal sealed class EnvironmentStub(string path):IWebHostEnvironment {public string ApplicationName{get;set;}="GaoApp.Web";public string EnvironmentName{get;set;}="Development";public string ContentRootPath{get;set;}=path;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public string WebRootPath{get;set;}=path;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
}

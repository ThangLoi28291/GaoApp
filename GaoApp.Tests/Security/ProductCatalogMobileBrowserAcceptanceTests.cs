using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProductCatalogMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Native_product_catalog_phone_controls_preserve_prices_units_barcodes_tenant_rights_and_store_state()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var seed = await ProductPriceQuickViewSqlServerTests.SeedAsync(app, store);
        var manager = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View, PermissionCodes.Catalog.Brand.View,
            PermissionCodes.Catalog.Category.View, PermissionCodes.Catalog.Unit.View, PermissionCodes.Catalog.Tax.View,
            PermissionCodes.Catalog.ProductAttribute.View, PermissionCodes.Catalog.AttributeValue.View, PermissionCodes.Catalog.Barcode.View);
        var denied = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var outsider = await app.AddAccountAsync(app.Stores[1], "*");
        var ids = new Dictionary<string,int>();
        int requestId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.Products.SingleAsync(x => x.Id == seed.ProductId);
            var brand = new Brand { StoreId = store.StoreId, Code = "UI14-BRAND", Name = "Thương hiệu kiểm thử UI-14", IsActive = true };
            var tax = new Tax { StoreId = store.StoreId, Code = "UI14-TAX", Name = "Thuế kiểm thử 8%", Rate = 8, IsActive = true };
            var attribute = new ProductAttribute { StoreId = store.StoreId, Code = "UI14-ATTR", Name = "Dung tích kiểm thử", Status = true };
            var value = new AttributeValue { StoreId = store.StoreId, Attribute = attribute, Code = "UI14-500", Name = "500 ml", Status = true };
            db.AddRange(brand, tax, attribute, value); product.Brand = brand; product.Tax = tax;
            var pack = await db.ProductUnitConversions.Include(x => x.Unit).SingleAsync(x => x.Id == seed.PackId);
            var request = new ProductBarcodeVerificationRequest { StoreId = store.StoreId, ProductVariantId = seed.VariantId,
                ProductUnitConversionId = pack.Id, ProductNameSnapshot = product.Name, UnitNameSnapshot = pack.Unit.Name,
                FactorSnapshot = pack.Factor, SuggestedBarcode = "2600140000048", RequestType = BarcodeVerificationRequestType.SupplierBarcode,
                RequestedByUserId = manager.UserId,
                RequestedAtUtc = DateTime.UtcNow, EmployeeNote = "Đề xuất trong dữ liệu kiểm thử UI-14" };
            db.Add(request);
            db.Add(new ProductVariantBarcodeHistory { StoreId = store.StoreId, ProductVariantId = seed.VariantId,
                ProductUnitConversionId = pack.Id, NewBarcode = "2600140000048", ActionType = BarcodeHistoryActionType.Assigned,
                ChangedAtUtc = DateTime.UtcNow, ChangedByUserId = manager.UserId, ChangedByUserName = manager.Name,
                Reason = "Lịch sử dữ liệu kiểm thử UI-14" });
            await db.SaveChangesAsync();
            ids["Product"] = product.Id; ids["Brand"] = brand.Id; ids["Category"] = product.CategoryId;
            ids["Unit"] = product.BaseUnitId; ids["Tax"] = tax.Id; ids["ProductAttribute"] = attribute.Id; ids["AttributeValue"] = value.Id;
            requestId = request.Id;
        }
        using var admin = await app.LoginAsync(manager);
        using var reader = await app.LoginAsync(viewer);
        using var blocked = await app.LoginAsync(denied);
        using var foreign = await app.LoginAsync(outsider);
        var before = await ProtectedStateAsync(app);
        var list = new[] { "Product", "Brand", "Category", "Unit", "Tax", "ProductAttribute", "AttributeValue" };
        foreach (var controller in list)
        {
            using var response = await blocked.Http.GetAsync($"/Admin/{controller}/Index");
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            using var create = await reader.Http.GetAsync($"/Admin/{controller}/Create");
            Assert.Equal(HttpStatusCode.Forbidden,create.StatusCode);
            using var edit = await reader.Http.GetAsync($"/Admin/{controller}/Edit/{ids[controller]}");
            Assert.Equal(HttpStatusCode.Forbidden,edit.StatusCode);
            using var cross = await foreign.Http.GetAsync($"/Admin/{controller}/Edit/{ids[controller]}");
            if (controller is "Product" or "Tax") Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
            else
            {
                Assert.Equal(HttpStatusCode.Found,cross.StatusCode);
                Assert.Equal($"/Admin/{controller}",cross.Headers.Location?.OriginalString.TrimEnd('/').Replace("/Index", ""));
            }
        }
        foreach (var path in new[] { "/admin/barcode-history", "/admin/barcode-normalization" })
        { using var response = await blocked.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        using (var cross = await foreign.Http.GetAsync($"/Admin/ProductUnitConversion/GetByVariantId?productVariantId={seed.VariantId}"))
        {
            Assert.Equal(HttpStatusCode.OK,cross.StatusCode);
            using var payload = JsonDocument.Parse(await cross.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
            Assert.Empty(payload.RootElement.GetProperty("data").EnumerateArray());
        }
        foreach (var command in new[] { "SaveConversion", "SaveBarcode" })
        {
            using var response = await reader.Http.PostAsync($"/Admin/ProductUnitConversion/{command}",
                new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        foreach (var command in new[] { "approve", "reject", "confirm-no-barcode" })
        {
            using var response = await reader.Http.PostAsync($"/admin/barcode-normalization/{requestId}/{command}",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        Assert.DoesNotContain("NCC-PRIVATE-ONLY",await reader.Http.GetStringAsync("/Admin/Product?lifecycle=all"));
        Assert.Contains("NCC-PRIVATE-ONLY",await admin.Http.GetStringAsync("/Admin/Product?lifecycle=all"));
        Assert.Equal(before,await ProtectedStateAsync(app));
        var root = FullApplicationFixture.SourceRoot();
        var output = Environment.GetEnvironmentVariable("GAO_UI14_REVIEW_OUTPUT") ?? MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-14-20261026","browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","product-catalog-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}",
            terminalId = store.TerminalId, output, manager = new { user = manager.Name, password = manager.Password },
            viewer = new { user = viewer.Name, password = viewer.Password }, ids, variantId = seed.VariantId, packId = seed.PackId, requestId }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(16));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);
        Assert.True(process.ExitCode == 0,log);
        Assert.Equal(before,await ProtectedStateAsync(app));
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new {
            passed = true, originalPricesUnitFactorsBarcodesProductAndSellReadinessPreserved = true,
            storesMenusSettingsRolesPermissionsAndOtherTenantPreserved = true,
            nativeViewCreateEditAndBarcodeApprovalRightsPreserved = true,
            nativeSupplierPrivacyPreserved = true, presentationWrites = 0, fixtureOnly = true
        },new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ProtectedStateAsync(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>() where TEntity : BaseEntity
        {
            var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            return rows.Select(row => db.Entry(row).Properties.OrderBy(x => x.Metadata.Name)
                .ToDictionary(property => property.Metadata.Name,property => property.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new { stores = await Rows<Store>(), menus = await Rows<AdminMenuItem>(), roles = await Rows<Role>(),
            grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.RoleId,x.PermissionId }).ToListAsync(),
            products = await Rows<Product>(), variants = await Rows<ProductVariant>(), brands = await Rows<Brand>(), categories = await Rows<Category>(),
            units = await Rows<Unit>(), taxes = await Rows<Tax>(), attributes = await Rows<ProductAttribute>(), values = await Rows<AttributeValue>(),
            conversions = await Rows<ProductUnitConversion>(), barcodes = await Rows<ProductVariantUnitBarcode>(), history = await Rows<ProductVariantBarcodeHistory>(),
            requests = await db.ProductBarcodeVerificationRequests.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            stock = await Rows<InventoryBalance>(), ledger = await Rows<InventoryTransaction>(), settings = await Rows<InvoiceProviderSetting>(),
            rewards = await Rows<RewardSettings>(), automatic = await Rows<AutoInvoiceSettings>() });
    }
}


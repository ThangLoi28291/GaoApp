using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptTemplateSqlServerTests
{
    [Fact]
    public async Task Only_admin_can_configure_shared_default_and_cashiers_receive_live_design_and_offline_snapshot()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, "*")); // Permission alone is insufficient.
        using var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        const string endpoint = "/admin/receipt-templates/default";
        var initial = await employee.JsonAsync(HttpMethod.Get, endpoint);
        Assert.Equal("modern-80", initial.GetProperty("template").GetProperty("key").GetString());
        using (var page = await employee.Http.GetAsync("/admin/receipt-templates")) Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        var design = new ReceiptDesign { Name = "Mẫu mặc định 45", PaperSize = "45", Title = "HÓA ĐƠN CHUNG" };
        using (var createDenied = await employee.Http.PostAsJsonAsync("/admin/receipt-templates/data", new SaveReceiptTemplateRequest(design, null)))
            Assert.Equal(HttpStatusCode.Forbidden, createDenied.StatusCode);
        var created = await admin.JsonAsync(HttpMethod.Post, "/admin/receipt-templates/data", new SaveReceiptTemplateRequest(design, null));
        var key = created.GetProperty("key").GetString()!;
        var version = created.GetProperty("rowVersion").GetString()!;
        var request = new SaveReceiptDefaultRequest(key, initial.GetProperty("rowVersion").GetString()!, version);
        using (var forbidden = await employee.Http.PutAsJsonAsync(endpoint, request)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var otherInitial = await foreign.JsonAsync(HttpMethod.Get, endpoint);
        using (var cross = await foreign.Http.PutAsJsonAsync(endpoint, request with { RowVersion = otherInitial.GetProperty("rowVersion").GetString()! }))
            Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        var saved = await admin.JsonAsync(HttpMethod.Put, endpoint, request);
        Assert.Equal(key, saved.GetProperty("template").GetProperty("key").GetString());
        Assert.Equal(saved.GetRawText(), (await employee.JsonAsync(HttpMethod.Get, endpoint)).GetRawText());
        using (var stale = await admin.Http.PutAsJsonAsync(endpoint, request with { Key = "classic-A4" })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var invalid = await admin.Http.PutAsJsonAsync(endpoint, request with { Key = "missing", RowVersion = saved.GetProperty("rowVersion").GetString()! })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var id = int.Parse(key[7..]);
        using (var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/admin/receipt-templates/data/{id}") { Content = JsonContent.Create(new { rowVersion = version }) })
        using (var deletion = await admin.Http.SendAsync(deleteRequest)) Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        var updated = await admin.JsonAsync(HttpMethod.Put, $"/admin/receipt-templates/data/{id}", new SaveReceiptTemplateRequest(design with { Title = "NỘI DUNG MỚI" }, version));
        var latest = await employee.JsonAsync(HttpMethod.Get, endpoint);
        Assert.Equal("NỘI DUNG MỚI", latest.GetProperty("template").GetProperty("design").GetProperty("title").GetString());
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        foreach (var path in new[] { "/admin/pos/offline/bootstrap", "/admin/pos/offline/status" })
            Assert.Equal(latest.GetRawText(), (await employee.JsonAsync(HttpMethod.Get, path)).GetProperty("receiptDefault").GetRawText());
        var orderId = draft.GetProperty("orderId").GetInt32();
        var print = await employee.Http.GetStringAsync($"/admin/pos/orders/{orderId}/print?autoPrint=false&size=A4");
        Assert.DoesNotContain("printTemplateChoice", print);
        Assert.DoesNotContain("href=\"/admin/receipt-templates\"", print);
        Assert.Contains("printTemplateName", print);
        Assert.Contains("receiptDefault", print);
        var other = await foreign.JsonAsync(HttpMethod.Get, endpoint);
        Assert.Equal("modern-80", other.GetProperty("template").GetProperty("key").GetString());
        // Invalid/missing tokens cannot change the default.
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var csrf = await admin.Http.PutAsJsonAsync(endpoint, request with { Key = "classic-A4", RowVersion = latest.GetProperty("rowVersion").GetString()! })) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    [Fact]
    public async Task Receipt_identity_migration_preserves_existing_store_data()
    {
        await using var database = new GaoApp.Tests.Configuration.InventoryPostingLocalDb();
        await database.MigrateAsync("20260910002000_AddPosReceiptTemplates");
        await database.ExecuteAsync("""
            INSERT dbo.Stores (Name, SubDomain, SubDomainNormalized, IsActive, IsDeleted, CreatedAtUtc)
            VALUES (N'Tiệm đang hoạt động', N'existing-receipt', N'EXISTING-RECEIPT', 1, 0, '2026-09-01');
            """);
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();
        var store = await db.Stores.SingleAsync();
        Assert.Equal("Tiệm đang hoạt động", store.Name);
        Assert.Equal("existing-receipt", store.SubDomain);
        Assert.True(store.IsActive);
        Assert.Null(store.ReceiptName); Assert.Null(store.ReceiptAddress); Assert.Null(store.ReceiptPhone);
    }

    [Fact]
    public async Task Store_receipt_identity_is_shared_scoped_versioned_and_used_by_receipts_and_offline_context()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store);
        using var manager = await app.LoginAsync(account);
        using var other = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.Reprint));
        const string url = "/admin/receipt-templates/store-info";
        var original = await cashier.JsonAsync(HttpMethod.Get, url);
        var originalOther = await other.JsonAsync(HttpMethod.Get, url);
        Assert.Equal("", original.GetProperty("storeAddress").GetString());
        Assert.Equal("", original.GetProperty("storePhone").GetString());
        var change = new SaveReceiptStoreInfoRequest { StoreName = " Tiệm Gạo An Bình ", StoreAddress = "12 Nguyễn Trãi\nPhường An Bình",
            StorePhone = "0909 123 456", RowVersion = original.GetProperty("rowVersion").GetString()! };
        using (var denied = await cashier.Http.PutAsJsonAsync(url, change)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await manager.Http.PutAsJsonAsync(url, change with { StoreName = "   " })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await manager.Http.PutAsJsonAsync(url, change with { StorePhone = new string('1', 51) })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var saved = await manager.JsonAsync(HttpMethod.Put, url, new { change.StoreName, change.StoreAddress, change.StorePhone, change.RowVersion, storeId = app.Stores[1].StoreId });
        Assert.Equal("Tiệm Gạo An Bình", saved.GetProperty("storeName").GetString());
        Assert.NotEqual(change.RowVersion, saved.GetProperty("rowVersion").GetString());
        Assert.Equal(saved.GetRawText(), (await cashier.JsonAsync(HttpMethod.Get, url)).GetRawText());
        Assert.Equal(originalOther.GetRawText(), (await other.JsonAsync(HttpMethod.Get, url)).GetRawText());
        using (var stale = await manager.Http.PutAsJsonAsync(url, change)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var csrf = await manager.Http.PutAsJsonAsync(url, change with { RowVersion = saved.GetProperty("rowVersion").GetString()! })) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        await manager.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await manager.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var receipt = await manager.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{draft.GetProperty("orderId").GetInt32()}/receipt");
        Assert.Equal(saved.GetProperty("storeName").GetString(), receipt.GetProperty("storeName").GetString());
        Assert.Equal(change.StoreAddress, receipt.GetProperty("storeAddress").GetString());
        Assert.Equal(change.StorePhone, receipt.GetProperty("storePhone").GetString());
        foreach (var endpoint in new[] { "/admin/pos/offline/bootstrap", "/admin/pos/offline/status" })
            Assert.Equal(saved.GetRawText(), (await manager.JsonAsync(HttpMethod.Get, endpoint)).GetProperty("receiptStoreInfo").GetRawText());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var persisted = await db.Stores.SingleAsync(x => x.Id == store.StoreId);
            Assert.Equal(original.GetProperty("storeName").GetString(), persisted.Name);
            Assert.Equal("Tiệm Gạo An Bình", persisted.ReceiptName);
            Assert.Equal(account.UserId, persisted.UpdatedBy);
        }
        var update = change with { RowVersion = saved.GetProperty("rowVersion").GetString()! };
        var concurrent = await Task.WhenAll(manager.Http.PutAsJsonAsync(url, update with { StoreName = "Tiệm A" }),
            manager.Http.PutAsJsonAsync(url, update with { StoreName = "Tiệm B" }));
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in concurrent) response.Dispose();
        var latest = await manager.JsonAsync(HttpMethod.Get, url);
        var cleared = await manager.JsonAsync(HttpMethod.Put, url, update with { StoreAddress = "", StorePhone = "", RowVersion = latest.GetProperty("rowVersion").GetString()! });
        Assert.Equal("", cleared.GetProperty("storeAddress").GetString());
        Assert.Equal("", cleared.GetProperty("storePhone").GetString());
    }

    [Fact]
    public async Task Templates_enforce_tenant_permissions_validation_and_concurrent_edit_versions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        using var manager = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[0]));
        using var other = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], PermissionCodes.Pos.Order.Reprint, PermissionCodes.Pos.Order.View));
        const string url = "/admin/receipt-templates/data";
        var builtins = await cashier.JsonAsync(HttpMethod.Get, url);
        Assert.Equal(16, builtins.GetArrayLength());
        var wide = Assert.Single(builtins.EnumerateArray(), x => x.GetProperty("design").GetProperty("layout").GetString() == "itemwide");
        Assert.Equal("itemwide-80", wide.GetProperty("key").GetString());
        Assert.Equal("80", wide.GetProperty("design").GetProperty("paperSize").GetString());
        Assert.All(builtins.EnumerateArray(), x => Assert.Equal("#000000", x.GetProperty("design").GetProperty("accentColor").GetString()));
        Assert.Equal(5, builtins.EnumerateArray().Select(x => x.GetProperty("design").GetProperty("paperSize").GetString()).Distinct().Count());
        var design = new ReceiptDesign { Name = "Mẫu riêng cửa hàng A", PaperSize = "45", AccentColor = "#dd3399", FooterText = new string('ệ', 400) };
        var create = new SaveReceiptTemplateRequest(design, null);
        using (var forbidden = await cashier.Http.PostAsJsonAsync(url, create)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var invalid = await manager.Http.PostAsJsonAsync(url, new SaveReceiptTemplateRequest(design with { AccentColor = "red; background:url(https://example.invalid)" }, null)))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var saved = await manager.JsonAsync(HttpMethod.Post, url, create);
        Assert.Equal("#000000", saved.GetProperty("design").GetProperty("accentColor").GetString());
        var key = saved.GetProperty("key").GetString()!;
        var id = int.Parse(key.Replace("custom-", ""));
        var version = saved.GetProperty("rowVersion").GetString();
        Assert.Equal(17, (await manager.JsonAsync(HttpMethod.Get, url)).GetArrayLength());
        Assert.Equal(16, (await other.JsonAsync(HttpMethod.Get, url)).GetArrayLength());
        using (var crossStore = await other.Http.PutAsJsonAsync(url + "/" + id, new SaveReceiptTemplateRequest(design, version)))
            Assert.Equal(HttpStatusCode.NotFound, crossStore.StatusCode);
        var updated = await manager.JsonAsync(HttpMethod.Put, url + "/" + id, new SaveReceiptTemplateRequest(design with { Name = "Tên hàng rộng tùy chỉnh", Layout = "itemwide", PaperSize = "A4" }, version));
        Assert.Equal("itemwide", updated.GetProperty("design").GetProperty("layout").GetString());
        Assert.Equal("80", updated.GetProperty("design").GetProperty("paperSize").GetString());
        var reloaded = (await manager.JsonAsync(HttpMethod.Get, url)).EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
        Assert.Equal(updated.GetProperty("design").GetRawText(), reloaded.GetProperty("design").GetRawText());
        using (var stale = await manager.Http.PutAsJsonAsync(url + "/" + id, new SaveReceiptTemplateRequest(design, version)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var csrf = await manager.Http.PostAsJsonAsync(url, create)) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        using (var request = new HttpRequestMessage(HttpMethod.Delete, url + "/" + id) { Content = JsonContent.Create(new { rowVersion = updated.GetProperty("rowVersion").GetString() }) })
        using (var deletion = await manager.Http.SendAsync(request)) Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Equal(16, (await manager.JsonAsync(HttpMethod.Get, url)).GetArrayLength());
        await using var db = app.Database.CreateTenantContext(app.Stores[0].StoreId);
        Assert.True((await db.Set<PosReceiptTemplate>().IgnoreQueryFilters().SingleAsync(x => x.Id == id)).IsDeleted);
        var legacyJson = System.Text.Json.JsonSerializer.Serialize(design, ReceiptTemplateService.Json);
        var legacy = new PosReceiptTemplate { StoreId = app.Stores[0].StoreId, Name = "Mẫu màu cũ", DefinitionJson = legacyJson };
        db.Add(legacy); await db.SaveChangesAsync();
        var legacyVersion = Convert.ToBase64String(legacy.RowVersion);
        var projected = (await manager.JsonAsync(HttpMethod.Get, url)).EnumerateArray().Single(x => x.GetProperty("key").GetString() == $"custom-{legacy.Id}");
        Assert.Equal("#000000", projected.GetProperty("design").GetProperty("accentColor").GetString());
        Assert.Equal(legacyVersion, projected.GetProperty("rowVersion").GetString());
        var persisted = await db.Set<PosReceiptTemplate>().AsNoTracking().SingleAsync(x => x.Id == legacy.Id);
        Assert.Equal(legacyJson, persisted.DefinitionJson);
        Assert.Equal(legacyVersion, Convert.ToBase64String(persisted.RowVersion));
    }
}

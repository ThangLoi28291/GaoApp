using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PreHostSecuritySqlServerTests
{
    [Fact]
    public async Task Employee_manager_cannot_discover_link_or_reset_other_store_accounts()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, PermissionCodes.Security.UserInStore.View,
            PermissionCodes.Security.UserInStore.Create, PermissionCodes.Security.UserInStore.Update);
        var foreign = await app.AddAccountAsync(app.Stores[1]);
        var local = await app.AddAccountAsync(store);
        using var client = await app.LoginAsync(manager);
        var lookup = await client.JsonAsync(HttpMethod.Get, "/Admin/UserInStores/SearchUsers?keyword=" + foreign.Name);
        Assert.Empty(lookup.EnumerateArray());
        using var link = await client.Http.PostAsync("/Admin/UserInStores/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = foreign.UserId.ToString(), ["RoleId"] = manager.RoleId.ToString(), ["IsActive"] = "true"
        }));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode); // Validation view, never a successful redirect.
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await db.UserInStores.AnyAsync(x => x.UserId == foreign.UserId));
        // A legitimate host-provisioned shared account must also be protected.
        db.UserInStores.Add(new GaoApp.Domain.Entities.UserInStore { StoreId = store.StoreId, UserId = foreign.UserId, RoleId = manager.RoleId });
        await db.SaveChangesAsync();
        var foreignHash = await db.Users.Where(x => x.Id == foreign.UserId).Select(x => x.PasswordHash).SingleAsync();
        var localHash = await db.Users.Where(x => x.Id == local.UserId).Select(x => x.PasswordHash).SingleAsync();
        foreach (var (userId, password) in new[] { (foreign.UserId, "Long-passphrase-for-test!"), (local.UserId, "123456") })
        {
            using var reset = await client.Http.PostAsync("/Admin/UserInStores/ResetPassword", PasswordForm(userId, password));
            Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        }
        Assert.Equal(foreignHash, await db.Users.Where(x => x.Id == foreign.UserId).Select(x => x.PasswordHash).SingleAsync());
        Assert.Equal(localHash, await db.Users.Where(x => x.Id == local.UserId).Select(x => x.PasswordHash).SingleAsync());
        using var allowedReset = await client.Http.PostAsync("/Admin/UserInStores/ResetPassword", PasswordForm(local.UserId, "New-local-passphrase!"));
        Assert.Equal(HttpStatusCode.Redirect, allowedReset.StatusCode);
        Assert.NotEqual(localHash, await db.Users.Where(x => x.Id == local.UserId).Select(x => x.PasswordHash).SingleAsync());
    }

    private static FormUrlEncodedContent PasswordForm(int userId, string password) => new(new Dictionary<string, string>
    {
        ["UserId"] = userId.ToString(), ["NewPassword"] = password, ["ConfirmPassword"] = password
    });

    [Fact]
    public async Task Underprivileged_user_is_denied_across_catalog_rewards_and_inventory_routes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        using var client = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0]));
        var routes = new[]
        {
            ("GET", "/Admin/Brand/Index"), ("GET", "/Admin/Unit/Index"), ("GET", "/Admin/Tax/Index"),
            ("GET", "/Admin/Product/Index"), ("GET", "/Admin/Product/GetUniqueAlias?input=test"),
            ("POST", "/Admin/Product/SaveVariants"), ("GET", "/Admin/Promotion/Index"),
            ("POST", "/Admin/Promotion/Save"), ("GET", "/Admin/DisplayPromotion/ActiveForCustomerDisplay"),
            ("POST", "/Admin/DisplayPromotion/Create"), ("GET", "/admin/api/suppliers/select2"),
            ("POST", "/admin/api/customers/manual-ledger"), ("POST", "/admin/api/customers/redeem-voucher"),
            ("GET", "/admin/api/customers/reward-vouchers/lookup?code=test"),
            ("POST", "/admin/api/customers/reward-vouchers/1/lock"),
            ("POST", "/admin/api/customers/reward-vouchers/1/unlock"),
            ("POST", "/admin/api/customers/reward-vouchers/1/cancel"),
            ("GET", "/admin/api/stock-counts/1"), ("PUT", "/admin/api/stock-counts/lines/1"),
            ("DELETE", "/admin/api/stock-counts/lines/1"), ("POST", "/admin/api/stock-counts/1/submit-approval"),
            ("POST", "/admin/api/stock-counts/1/reject"), ("POST", "/admin/api/stock-counts/1/refresh-system-qty")
        };
        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await client.Http.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Real_permissions_allow_intended_actions_and_revoke_without_relogin()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, PermissionCodes.Catalog.Brand.Create);
        using var client = await app.LoginAsync(account);
        using var created = await client.Http.PostAsync("/Admin/Brand/Create", BrandForm("AUTHORIZED-PROBE"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.True(await db.Brands.AnyAsync(x => x.Code == "AUTHORIZED-PROBE"));
        using var edit = await client.Http.PostAsync("/Admin/Brand/Edit", BrandForm("AUTHORIZED-PROBE"));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == account.RoleId).ToListAsync());
        await db.SaveChangesAsync();
        using var revoked = await client.Http.PostAsync("/Admin/Brand/Create", BrandForm("REVOKED-PROBE"));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.False(await db.Brands.AnyAsync(x => x.Code == "REVOKED-PROBE"));
    }

    [Fact]
    public async Task Any_permission_supports_each_editor_role_but_combined_upsert_requires_both_permissions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        foreach (var permission in new[] { PermissionCodes.Catalog.Product.Create, PermissionCodes.Catalog.Product.Update })
        {
            using var editor = await app.LoginAsync(await app.AddAccountAsync(store, permission));
            using var alias = await editor.Http.GetAsync("/Admin/Product/GetUniqueAlias?input=test");
            Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
            editor.Http.DefaultRequestHeaders.Host = app.Stores[1].Host;
            using var otherStore = await editor.Http.GetAsync("/Admin/Product/GetUniqueAlias?input=test");
            Assert.True(otherStore.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
        foreach (var permissions in new[] {
                     new[] { PermissionCodes.Catalog.ProductVariant.Create },
                     new[] { PermissionCodes.Catalog.ProductVariant.Update },
                     new[] { PermissionCodes.Catalog.ProductVariant.Create, PermissionCodes.Catalog.ProductVariant.Update } })
        {
            using var editor = await app.LoginAsync(await app.AddAccountAsync(store, permissions));
            using var save = await editor.Http.PostAsJsonAsync("/Admin/Product/SaveVariants", new { productId = 0 });
            Assert.Equal(permissions.Length == 2 ? HttpStatusCode.OK : HttpStatusCode.Forbidden, save.StatusCode);
        }
        using var approver = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Purchase.Receipt.Approve));
        using var lookup = await approver.Http.GetAsync("/admin/api/suppliers/select2?term=test");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var display = await cashier.Http.GetAsync("/Admin/DisplayPromotion/ActiveForCustomerDisplay");
        Assert.Equal(HttpStatusCode.OK, display.StatusCode);
    }

    [Fact]
    public async Task Retired_inventory_endpoint_requires_csrf_even_for_approver_and_never_writes_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.Adjustment.Approve));
        using var retired = await client.Http.PostAsJsonAsync("/admin/api/inventory/transactions", new { quantityChange = 100 });
        Assert.Equal(HttpStatusCode.Gone, retired.StatusCode);
        Assert.Contains("INVENTORY_DOCUMENT_REQUIRED", await retired.Content.ReadAsStringAsync());
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var forged = await client.Http.PostAsJsonAsync("/admin/api/inventory/transactions", new { quantityChange = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100m, await db.InventoryBalances.Where(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId).Select(x => x.OnHandQty).SingleAsync());
        using var protectedResponse = await client.Http.GetAsync("/Admin/Brand/Index");
        Assert.True(protectedResponse.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", protectedResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", protectedResponse.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'self'", protectedResponse.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(protectedResponse.Headers.Contains("Server"));
    }

    private static FormUrlEncodedContent BrandForm(string code) => new(new Dictionary<string, string>
    {
        ["Code"] = code, ["Name"] = "Permission probe", ["Status"] = "true"
    });

    [Fact]
    public async Task User_without_permissions_cannot_create_catalog_or_invent_inventory_transactions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store));
        using var brand = await client.Http.PostAsync("/Admin/Brand/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = "SECURITY-PROBE", ["Name"] = "Permission probe", ["Status"] = "true"
        }));
        using var inventory = await client.Http.PostAsJsonAsync("/admin/api/inventory/transactions", new
        {
            warehouseId = store.WarehouseId, productVariantId = store.VariantId,
            transactionType = InventoryTransactionType.AdjustmentIncrease,
            referenceType = InventoryReferenceType.Adjustment, referenceId = "SECURITY-PROBE", quantityChange = 1
        });
        Assert.True(brand.StatusCode == HttpStatusCode.Forbidden && inventory.StatusCode == HttpStatusCode.Forbidden,
            $"Unauthorized requests: Brand={brand.StatusCode}, raw inventory={inventory.StatusCode}");
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await db.Brands.AnyAsync(x => x.Code == "SECURITY-PROBE"));
        Assert.Equal(100m, await db.InventoryBalances.Where(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId).Select(x => x.OnHandQty).SingleAsync());
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Kiosk;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class KioskSqlServerTests
{
    internal sealed record Seed(int StationId, string Key, int VariantId, int PackId, string Barcode, int CustomerId, int OrderId);
    internal static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int userId)
    {
        var customer = await CustomerProfileSqlServerTests.SeedAsync(app, store, userId);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var branch = await db.Stores.SingleAsync(x => x.Id == store.StoreId); branch.Name = "GAO MART";
        var product = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit).SingleAsync(x => x.Id == store.VariantId);
        product.HasInputInvoice = true; product.Price = 5000; product.Product.BasePrice = 5000;
        product.Product.Name = "Nước suối Aquafina 500ml"; product.ProductVariantName = "Nước suối Aquafina 500ml";
        product.Product.Description = "Nước uống đóng chai. Bảo quản nơi khô ráo, thoáng mát.";
        product.Product.BaseUnit.Name = "Chai";
        var single = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = product.Id, UnitId = product.Product.BaseUnitId, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 5000 };
        single.Barcodes.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8934588063053", IsPrimary = true, IsActive = true });
        var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = product.Id, Unit = new Unit { StoreId = store.StoreId, Code = "THUNG", Name = "Thùng" }, Factor = 24, Price = 100000 };
        db.AddRange(single, pack);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "KIOSK-01", Name = "Quầy tự phục vụ 01" };
        var key = KioskAccess.Secret();
        var station = new KioskStation { StoreId = store.StoreId, Terminal = terminal, WarehouseId = store.WarehouseId,
            ActivationHash = KioskAccess.Hash(key), ActivationExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            SystemUser = new User { UserName = "kiosk_test_" + Guid.NewGuid().ToString("N"), FullName = "Quầy tự phục vụ 01", IsActive = false, PasswordHash = "!NO-LOGIN!" } };
        db.Add(station);
        var bankAccount = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST-ONLY", AccountName = "Disposable test" };
        db.Add(bankAccount); await db.SaveChangesAsync();
        db.Add(new StoreAcbSettings { StoreId = store.StoreId, Enabled = true, BankAccountId = bankAccount.Id }); // No credentials or live bank calls in this fixture.
        db.Add(new DisplayPromotion { StoreId = store.StoreId, Title = "Mua sắm vui hơn mỗi ngày", Description = "Ưu đãi dành cho bạn tại Gao Mart", MediaType = "text", DurationSeconds = 15 });
        await db.SaveChangesAsync();
        return new(station.Id, key, product.Id, pack.Id, "8934588063053", customer.CustomerId, customer.OrderId);
    }
    internal static async Task SetTokenAsync(FullApplicationFixture.Client client)
    {
        var html = await client.Http.GetStringAsync("/kiosk");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success); client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value));
    }
    private static object Command(JsonElement state, string action, object? values = null, Guid? id = null)
    {
        var result = new Dictionary<string, object?> { ["sessionKey"] = state.GetProperty("sessionKey").GetGuid(), ["revision"] = state.GetProperty("revision").GetInt64(), ["commandId"] = id ?? Guid.NewGuid(), ["action"] = action };
        if (values != null) foreach (var p in JsonSerializer.SerializeToElement(values).EnumerateObject()) result[p.Name] = p.Value.Clone();
        return result;
    }
    [Theory]
    [InlineData("scan", "allowed")]
    [InlineData("add", "allowed")]
    [InlineData("scan", "no-invoice")]
    [InlineData("add", "no-invoice")]
    [InlineData("scan", "inactive")]
    [InlineData("add", "inactive")]
    [InlineData("scan", "soft-deleted")]
    [InlineData("add", "soft-deleted")]
    [InlineData("scan", "missing")]
    [InlineData("add", "missing")]
    [InlineData("scan", "other-store")]
    [InlineData("add", "other-store")]
    [InlineData("scan", "deleted-line")]
    [InlineData("add", "deleted-line")]
    public async Task Cart_add_requires_an_active_same_store_invoice_variant_for_every_existing_line(string action, string principalState)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*"); var seed = await SeedAsync(app, store, account.UserId);
        using var machine = app.Anonymous(store); await SetTokenAsync(machine);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key = seed.Key });
        var state = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "start"));
        var orderId = state.GetProperty("order").GetProperty("id").GetInt32();
        var revision = state.GetProperty("revision").GetInt64();
        int existingLineId, originalVariantId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var productId = await db.ProductVariants.Where(x => x.Id == seed.VariantId).Select(x => x.ProductId).SingleAsync();
            var variant = new ProductVariant { StoreId = store.StoreId, ProductId = productId,
                Sku = "KIOSK-GUARD-" + Guid.NewGuid().ToString("N"), Price = 5000, IsActive = true, HasInputInvoice = true };
            db.ProductVariants.Add(variant); await db.SaveChangesAsync(); originalVariantId = variant.Id;
            var line = new OrderLine { StoreId = store.StoreId, OrderId = orderId, ProductId = productId,
                VariantId = variant.Id, ItemName = "Existing kiosk item", Quantity = 2, BaseQuantity = 2,
                Multiplier = 1, UnitPrice = 5000, OriginalUnitPrice = 5000, LineTotal = 10000 };
            db.OrderLines.Add(line);
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            order.Subtotal = order.GrandTotal = principalState == "deleted-line" ? 0 : line.LineTotal;
            await db.SaveChangesAsync(); existingLineId = line.Id;
            variant.HasInputInvoice = principalState is not ("no-invoice" or "deleted-line");
            variant.IsActive = principalState != "inactive";
            variant.IsDeleted = principalState == "soft-deleted";
            line.IsDeleted = principalState == "deleted-line";
            await db.SaveChangesAsync();
            Assert.Equal(principalState == "deleted-line", await db.OrderLines.IgnoreQueryFilters()
                .Where(x => x.Id == existingLineId).Select(x => x.IsDeleted).SingleAsync());
            Assert.True(await db.ProductVariants.AnyAsync(x => x.Id == seed.VariantId && x.IsActive && x.HasInputInvoice));
        }
        if (principalState == "other-store")
        {
            await using var db = app.Database.CreateHostContext();
            var foreignVariant = await db.ProductVariants.SingleAsync(x => x.Id == app.Stores[1].VariantId);
            foreignVariant.HasInputInvoice = true; await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [OrderLines] SET [VariantId] = {foreignVariant.Id} WHERE [Id] = {existingLineId} AND [StoreId] = {store.StoreId} AND [OrderId] = {orderId}");
        }
        var missingPrincipal = principalState == "missing";
        try
        {
            if (missingPrincipal)
            {
                // Only this fixture's GUID database can contain the deliberately broken legacy FK.
                await using var db = app.Database.CreateHostContext();
                Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", db.Database.GetDbConnection().Database);
                Assert.False(await db.ProductVariants.IgnoreQueryFilters().AnyAsync(x => x.Id == int.MaxValue));
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OrderLines] NOCHECK CONSTRAINT [FK_OrderLines_ProductVariant_VariantId]");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [OrderLines] SET [VariantId] = {int.MaxValue} WHERE [Id] = {existingLineId} AND [StoreId] = {store.StoreId} AND [OrderId] = {orderId}");
            }
            var allowed = principalState is "allowed" or "deleted-line";
            using var response = await machine.Http.PostAsJsonAsync("/kiosk/api/command",
                Command(state, action, new { barcode = seed.Barcode, variantId = seed.VariantId, quantity = 1 }));
            var responseText = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == (allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest), responseText);
            if (!allowed) Assert.Contains("Sản phẩm hoặc quà tặng này chưa hỗ trợ thanh toán QR tự động.",
                JsonDocument.Parse(responseText).RootElement.GetProperty("message").GetString());

            await using var verify = app.Database.CreateTenantContext(store.StoreId);
            var savedStation = await verify.Set<KioskStation>().SingleAsync(x => x.Id == seed.StationId);
            var savedOrder = await verify.Orders.SingleAsync(x => x.Id == orderId);
            var savedLines = await verify.OrderLines.Where(x => x.OrderId == orderId).ToListAsync();
            Assert.Equal(orderId, savedStation.OrderId);
            Assert.Equal(revision + (allowed ? 1 : 0), savedStation.Revision);
            Assert.Equal(allowed ? (principalState == "allowed" ? 15000 : 5000) : 10000, savedOrder.GrandTotal);
            Assert.Equal(OrderStatus.Draft, savedOrder.Status);
            Assert.Equal(0, savedOrder.PaidTotal);
            Assert.Equal(allowed ? (principalState == "allowed" ? 2 : 1) : 1, savedLines.Count);
            if (principalState != "deleted-line") Assert.Equal(2, savedLines.Single(x => x.Id == existingLineId).Quantity);
            if (allowed) Assert.Equal(1, savedLines.Single(x => x.VariantId == seed.VariantId).Quantity);
            else Assert.DoesNotContain(savedLines, x => x.VariantId == seed.VariantId);
            Assert.False(await verify.OrderPayments.AnyAsync(x => x.OrderId == orderId));
            Assert.False(await verify.PosPaymentQrRequests.AnyAsync(x => x.OrderId == orderId));
            Assert.False(await verify.InventoryTransactions.AnyAsync(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == orderId.ToString()));
        }
        finally
        {
            if (missingPrincipal)
            {
                await using var db = app.Database.CreateHostContext();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [OrderLines] SET [VariantId] = {originalVariantId} WHERE [Id] = {existingLineId} AND [StoreId] = {store.StoreId} AND [OrderId] = {orderId}");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OrderLines] WITH CHECK CHECK CONSTRAINT [FK_OrderLines_ProductVariant_VariantId]");
                Assert.Equal(1, await db.Database.SqlQuery<int>($"""
                    SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
                    WHERE [name] = 'FK_OrderLines_ProductVariant_VariantId'
                        AND [parent_object_id] = OBJECT_ID('OrderLines') AND [is_disabled] = 0 AND [is_not_trusted] = 0
                    """).SingleAsync());
            }
        }
    }
    [Fact]
    public async Task Activation_is_one_use_device_and_store_scoped_cart_retries_keep_pack_price_and_do_not_post_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*"); var seed = await SeedAsync(app, store, account.UserId);
        using var machine = app.Anonymous(store); await SetTokenAsync(machine);
        using (var denied = await machine.Http.GetAsync("/kiosk/api/state")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var noCsrf = app.Anonymous(store)) {
            using var response = await noCsrf.Http.PostAsJsonAsync("/kiosk/activate", new { key = seed.Key });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key = seed.Key });
        using (var replay = app.Anonymous(store)) {
            await SetTokenAsync(replay);
            using var response = await replay.Http.PostAsJsonAsync("/kiosk/activate", new { key = seed.Key }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var other = app.Anonymous(app.Stores[1])) {
            foreach (Cookie cookie in machine.Cookies.GetCookies(new Uri(app.Address, "/kiosk"))) other.Cookies.Add(app.Address, new Cookie(cookie.Name, cookie.Value, cookie.Path));
            using var response = await other.Http.GetAsync("/kiosk/api/state"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using (var admin = await machine.Http.GetAsync("/admin/kiosks")) Assert.False(admin.IsSuccessStatusCode);
        var products = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/products?q=nuoc%20suoi");
        Assert.Single(products.EnumerateArray()); Assert.Equal(2, products[0].GetProperty("units").GetArrayLength());
        Assert.False(products[0].TryGetProperty("costPrice", out _));
        int variantImageId;
        await using (var imageDb = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await imageDb.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == seed.VariantId);
            var productImage = new ProductImage { StoreId = store.StoreId, ProductId = variant.ProductId, IsPrimary = true,
                MediaAsset = new MediaAsset { StoreId = store.StoreId, StoragePath = "uploads/test/cart-product.png" } };
            var variantImage = new ProductImage { StoreId = store.StoreId, ProductId = variant.ProductId,
                MediaAsset = new MediaAsset { StoreId = store.StoreId, StoragePath = "uploads/test/cart-variant.png" } };
            imageDb.AddRange(productImage, variantImage); await imageDb.SaveChangesAsync();
            variantImageId = variantImage.Id; variant.PrimaryProductImageId = variantImageId; await imageDb.SaveChangesAsync();
        }
        var state = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "start"));
        Assert.Equal("shop", state.GetProperty("mode").GetString());
        var add = Command(state, "scan", new { barcode = seed.Barcode, quantity = 24 });
        var staleCommand = Command(state, "scan", new { barcode = seed.Barcode, quantity = 1 });
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", add);
        Assert.Equal(100000, state.GetProperty("order").GetProperty("grandTotal").GetDecimal());
        Assert.Equal("/uploads/test/cart-variant.png", state.GetProperty("order").GetProperty("lines")[0].GetProperty("imageUrl").GetString());
        await using (var imageDb = app.Database.CreateTenantContext(store.StoreId))
        {
            (await imageDb.ProductImages.SingleAsync(x => x.Id == variantImageId)).IsDeleted = true; await imageDb.SaveChangesAsync();
        }
        var reloadedImages = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
        Assert.Equal("/uploads/test/cart-product.png", reloadedImages.GetProperty("order").GetProperty("lines")[0].GetProperty("imageUrl").GetString());
        var retries = await Task.WhenAll(machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", add), machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", add));
        var duplicate = retries[0];
        Assert.Equal(retries[0].GetProperty("revision").GetInt64(), retries[1].GetProperty("revision").GetInt64());
        Assert.Equal(state.GetProperty("revision").GetInt64(), duplicate.GetProperty("revision").GetInt64());
        Assert.Equal(24, duplicate.GetProperty("order").GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        using (var stale = await machine.Http.PostAsJsonAsync("/kiosk/api/command", staleCommand)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var stale = await machine.Http.PostAsJsonAsync("/kiosk/api/command", Command(duplicate, "scan", new { barcode = seed.Barcode, quantity = 999 }))) Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        state = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
        Assert.Equal(24, state.GetProperty("order").GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        using (var invalidFinish = await machine.Http.PostAsJsonAsync("/kiosk/api/command", Command(state, "finish"))) Assert.Equal(HttpStatusCode.Conflict, invalidFinish.StatusCode);
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "cancel")); Assert.Equal("idle", state.GetProperty("mode").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var s = await db.Set<KioskStation>().SingleAsync(); Assert.Null(s.OrderId);
            var order = await db.Orders.Include(x => x.POSShift).SingleAsync(x => x.POSShift.TerminalId == s.TerminalId);
            Assert.Equal(GaoApp.Domain.Enums.OrderStatus.Cancelled, order.Status);
            Assert.Equal(s.SystemUserId, order.POSShift.OpenedByUserId);
            Assert.False(await db.OrderPayments.AnyAsync(x => x.OrderId == order.Id));
            Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ReferenceType == GaoApp.Domain.Enums.InventoryReferenceType.Order && x.ReferenceId == order.Id.ToString()));
            s.IsActive = false; await db.SaveChangesAsync();
        }
        using var revoked = await machine.Http.GetAsync("/kiosk/api/products?q=nuoc"); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }
    [Fact]
    public async Task Customer_exact_phone_lookup_is_read_only_private_and_expires()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*"); var seed = await SeedAsync(app, store, account.UserId);
        using var machine = app.Anonymous(store); await SetTokenAsync(machine);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key = seed.Key });
        using (var partial = await machine.Http.PostAsJsonAsync("/kiosk/api/customer", new { phone = "090123" } )) Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        var customer = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer", new { phone = "+84 901 234 567" });
        Assert.Equal("Nguyễn Minh An", customer.GetProperty("name").GetString()); Assert.False(customer.TryGetProperty("note", out _));
        Assert.Equal(11, customer.GetProperty("rewards").GetProperty("availablePoints").GetDecimal());
        var ledger = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer/history", new { tab = "points", page = 2 });
        Assert.Equal(23, ledger.GetProperty("total").GetInt32()); Assert.Equal(3, ledger.GetProperty("items").GetArrayLength());
        Assert.False(ledger.GetProperty("items")[0].TryGetProperty("employee", out _));
        var orders = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer/history", new { tab = "orders" }); Assert.Equal(3, orders.GetProperty("total").GetInt32());
        var voucherId = (await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer/history", new { tab = "vouchers" })).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("status").GetInt32() == 2).GetProperty("id").GetInt32();
        var voucher = await machine.JsonAsync(HttpMethod.Post, $"/kiosk/api/customer/voucher/{voucherId}", new { }); Assert.Equal("PROFILE-SALE", voucher.GetProperty("usedOrderNumber").GetString());
        using (var wrongVoucher = await machine.Http.PostAsJsonAsync("/kiosk/api/customer/voucher/2147483647", new { })) Assert.Equal(HttpStatusCode.NotFound, wrongVoucher.StatusCode);
        await machine.JsonAsync(HttpMethod.Post, $"/kiosk/api/customer/order/{seed.OrderId}", new { });
        using (var wrong = await machine.Http.PostAsJsonAsync("/kiosk/api/customer/order/2147483647", new { })) Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var station = await db.Set<KioskStation>().SingleAsync(); station.CustomerExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        using (var expired = await machine.Http.PostAsJsonAsync("/kiosk/api/customer/history", new { tab = "points" })) Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer", new { phone = "0901234567" });
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/customer/end", new { });
        using (var ended = await machine.Http.PostAsJsonAsync("/kiosk/api/customer/history", new { tab = "vouchers" })) Assert.Equal(HttpStatusCode.Forbidden, ended.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(23, await verify.CustomerRewardLedgers.CountAsync()); Assert.Equal(11500, await verify.CustomerRewardLedgers.SumAsync(x => x.Amount));
        Assert.Null((await verify.Set<KioskStation>().SingleAsync()).CustomerId);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Completed_kiosk_order_defaults_to_automatic_invoice_with_audit_and_preserves_staff_choice(bool finishDirectly, bool manual)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*"); var seed = await SeedAsync(app, store, account.UserId);
        using var machine = app.Anonymous(store); await SetTokenAsync(machine);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key = seed.Key });
        var state = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "start"));
        Assert.Equal("Unselected", state.GetProperty("order").GetProperty("invoiceIssuanceRoute").GetString());
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "scan", new { barcode = seed.Barcode }));
        var orderId = state.GetProperty("order").GetProperty("id").GetInt32();
        int systemUserId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            systemUserId = (await db.Set<KioskStation>().SingleAsync()).SystemUserId;
            // Isolate invoice routing on an order already finalized by the payment workflow.
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            order.Status = OrderStatus.Completed; order.PaymentStatus = PaymentStatus.Paid; order.PaidTotal = order.GrandTotal;
            if (manual) { order.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual; order.InvoiceIssuanceRouteSelectedByUserId = account.UserId; }
            await db.SaveChangesAsync();
        }
        if (!finishDirectly)
        {
            state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/poll", new { recentActivity = true });
            Assert.Equal(manual ? "Manual" : "Automatic", state.GetProperty("order").GetProperty("invoiceIssuanceRoute").GetString());
            state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/poll", new { recentActivity = true });
        }
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "finish"));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        var saved = await verify.Orders.SingleAsync(x => x.Id == orderId);
        Assert.Equal(manual ? InvoiceIssuanceRoute.Manual : InvoiceIssuanceRoute.Automatic, saved.InvoiceIssuanceRoute);
        Assert.Equal(manual ? account.UserId : systemUserId, saved.InvoiceIssuanceRouteSelectedByUserId);
        if (!manual) Assert.NotNull(saved.InvoiceIssuanceRouteSelectedAtUtc);
        Assert.Null((await verify.Set<KioskStation>().SingleAsync()).OrderId);
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "start"));
        Assert.NotEqual(orderId, state.GetProperty("order").GetProperty("id").GetInt32());
        Assert.Equal("shop", state.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("qr").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("completedAtUtc").ValueKind);
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "scan", new { barcode = seed.Barcode, quantity = 24 }));
        Assert.Equal(100000, state.GetProperty("order").GetProperty("grandTotal").GetDecimal());
        var lineId = state.GetProperty("order").GetProperty("lines")[0].GetProperty("id").GetInt32();
        state = await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "quantity", new { lineId, quantity = 0 }));
        Assert.Equal(0, state.GetProperty("order").GetProperty("grandTotal").GetDecimal());
        Assert.Empty(state.GetProperty("order").GetProperty("lines").EnumerateArray());
        Assert.Equal(0, state.GetProperty("order").GetProperty("paidTotal").GetDecimal());
    }
    [Fact]
    public async Task Admin_manages_only_own_store_devices_and_pause_help_rotation_are_enforced()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store);
        using var admin = await app.LoginAsync(account);
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreignAdmin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        int terminalId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "SELF-CHECKOUT", Name = "Máy tự phục vụ" };
            db.Add(terminal); await db.SaveChangesAsync(); terminalId = terminal.Id;
        }
        using (var denied = await staff.Http.GetAsync("/admin/kiosks")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var created = await admin.Http.PostAsync("/admin/kiosks/create", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["TerminalId"] = terminalId.ToString(), ["WarehouseId"] = store.WarehouseId.ToString() }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var key = Regex.Match(await created.Content.ReadAsStringAsync(), @"<textarea[^>]*>([^<]+)</textarea>").Groups[1].Value.Trim(); Assert.Equal(64, key.Length);
        int stationId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var station = await db.Set<KioskStation>().Include(x => x.SystemUser).SingleAsync(); stationId = station.Id;
            Assert.False(station.SystemUser.IsActive); Assert.False(await db.UserInStores.AnyAsync(x => x.UserId == station.SystemUserId));
            Assert.Null(station.DeviceHash); Assert.NotEqual(key, station.ActivationHash);
        }
        Assert.Contains("data-kiosk-id", await admin.Http.GetStringAsync("/admin/kiosks"));
        Assert.Empty((await foreignAdmin.JsonAsync(HttpMethod.Get, "/admin/kiosks/status")).EnumerateArray());
        using (var foreign = await foreignAdmin.Http.PostAsync($"/admin/kiosks/{stationId}/revoke", new FormUrlEncodedContent([]))) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var machine = app.Anonymous(store); await SetTokenAsync(machine);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key });
        using (var pause = await admin.Http.PostAsync($"/admin/kiosks/{stationId}/pause", new FormUrlEncodedContent([]))) Assert.Equal(HttpStatusCode.Redirect, pause.StatusCode);
        var state = await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state"); Assert.True(state.GetProperty("isPaused").GetBoolean());
        using (var start = await machine.Http.PostAsJsonAsync("/kiosk/api/command", Command(state, "start"))) Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/api/command", Command(state, "help"));
        var monitor = (await admin.JsonAsync(HttpMethod.Get, "/admin/kiosks/status"))[0];
        Assert.NotEqual(JsonValueKind.Null, monitor.GetProperty("helpRequestedAtUtc").ValueKind); Assert.False(monitor.TryGetProperty("deviceHash", out _));
        using (var done = await admin.Http.PostAsync($"/admin/kiosks/{stationId}/help-done", new FormUrlEncodedContent([]))) Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        using (var revoke = await admin.Http.PostAsync($"/admin/kiosks/{stationId}/revoke", new FormUrlEncodedContent([]))) Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        using var noAccess = await machine.Http.GetAsync("/kiosk/api/state"); Assert.Equal(HttpStatusCode.Unauthorized, noAccess.StatusCode);
        using var rotated = await admin.Http.PostAsync($"/admin/kiosks/{stationId}/key", new FormUrlEncodedContent([])); Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newKey = Regex.Match(await rotated.Content.ReadAsStringAsync(), @"<textarea[^>]*>([^<]+)</textarea>").Groups[1].Value.Trim(); Assert.NotEqual(key, newKey);
        using (var oldKey = await machine.Http.PostAsJsonAsync("/kiosk/activate", new { key })) Assert.Equal(HttpStatusCode.BadRequest, oldKey.StatusCode);
        await machine.JsonAsync(HttpMethod.Post, "/kiosk/activate", new { key = newKey }); await machine.JsonAsync(HttpMethod.Get, "/kiosk/api/state");
    }
}

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class DesktopHomeAcceptanceTests
{
    [Fact]
    public async Task Desktop_workspace_uses_configured_menu_and_authorized_sales_while_phone_pixels_and_store_data_stay_unchanged()
    {
        var root = FullApplicationFixture.SourceRoot();
        var baseline = Environment.GetEnvironmentVariable("GAO_DESKTOP_HOME_BASELINE")
            ?? Path.Combine(root, "publish", "releases", "20261029-045916-e983c1a5");
        var candidate = Environment.GetEnvironmentVariable("GAO_DESKTOP_HOME_RELEASE")
            ?? throw new InvalidOperationException("GAO_DESKTOP_HOME_RELEASE must select the verified WebOnly package.");
        await using var app = new PublishedProductionSmokeTests.PublishedFixture(baseline);
        await app.Database.MigrateAsync();
        var shop = await app.Database.SeedInventoryCatalogAsync();
        var foreign = await app.Database.SeedInventoryCatalogAsync();
        const string password = "DesktopFixture!29October2026";
        var accounts = new Dictionary<string, object>();
        int terminalId, foreignTerminalId;
        await using (var db = app.Database.CreateHostContext())
        {
            var shopStore = await db.Stores.SingleAsync(x => x.Id == shop.StoreId);
            shopStore.SubDomain = "shop"; shopStore.SubDomainNormalized = "SHOP";
            var otherStore = await db.Stores.SingleAsync(x => x.Id == foreign.StoreId);
            otherStore.SubDomain = "other"; otherStore.SubDomainNormalized = "OTHER";
            await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedPermissionsAsync(db);
            async Task<(User User, Role Role, UserInStore Membership)> Account(string name, string fullName, int storeId, params string[] permissions)
            {
                var role = new Role { StoreId = storeId, Code = name.ToUpperInvariant(), Name = fullName };
                var user = new User { UserName = name, FullName = fullName, PasswordHash = new PasswordHasher().Hash(password) };
                var member = new UserInStore { StoreId = storeId, User = user, Role = role }; db.Add(member);
                var query = db.Permissions.AsQueryable();
                if (!permissions.Contains("*")) query = query.Where(p => permissions.Contains(p.Code));
                foreach (var id in await query.Select(p => p.Id).ToArrayAsync()) db.Add(new RolePermission { Role = role, PermissionId = id });
                await db.SaveChangesAsync(); accounts[name] = new { user = name, password };
                return (user, role, member);
            }
            var manager = await Account("home_manager", "Nguyễn Minh Anh", shop.StoreId, "*");
            var staff = await Account("home_staff", "Nhân viên ca sáng", shop.StoreId, PermissionCodes.Admin.DashboardView,
                PermissionCodes.Pos.Order.View, PermissionCodes.Catalog.Product.View, PermissionCodes.Catalog.Customer.View, PermissionCodes.Inventory.Transaction.View);
            await Account("home_denied", "Tài khoản không có trang chủ", shop.StoreId, PermissionCodes.Pos.Order.View);
            var foreignOwner = await Account("home_foreign", "Quản lý cửa hàng khác", foreign.StoreId, "*");
            var terminal = new POSTerminal { StoreId = shop.StoreId, Code = "HOME", Name = "Quầy chính" };
            var otherTerminal = new POSTerminal { StoreId = foreign.StoreId, Code = "OTHER", Name = "Quầy cửa hàng khác" };
            db.AddRange(terminal, otherTerminal); await db.SaveChangesAsync(); terminalId = terminal.Id; foreignTerminalId = otherTerminal.Id;
            AdminMenuItem Group(string title, int order, string icon) => new() { StoreId = shop.StoreId, Title = title, SortOrder = order, Icon = icon };
            AdminMenuItem Menu(AdminMenuItem group, string title, string url, string permission, int order, string icon) => new() {
                StoreId = shop.StoreId, Parent = group, Title = title, Url = url, PermissionCode = permission, SortOrder = order, Icon = icon };
            var sales = Group("Bán hàng", 1, "bx bx-cart"); var stock = Group("Hàng hóa & kho", 2, "bx bx-package");
            var management = Group("Điều hành", 3, "bx bx-line-chart");
            var pos = Menu(sales, "Bán hàng POS", "/admin/pos/v3", PermissionCodes.Pos.Order.View, 1, "bx bx-cart");
            var orders = Menu(sales, "Đơn bán hàng", "/admin/pos/orders-page", PermissionCodes.Pos.Order.View, 2, "bx bx-receipt");
            var customers = Menu(sales, "Khách hàng", "/admin/customers", PermissionCodes.Catalog.Customer.View, 3, "bx bx-user");
            var products = Menu(stock, "Sản phẩm", "/admin/products?sort=name&status=active", PermissionCodes.Catalog.Product.View, 1, "bx bx-box");
            var receiving = Menu(stock, "Nhập hàng", "/admin/stock-documents", PermissionCodes.Inventory.StockDocument.View, 2, "bx bx-import");
            var ledger = Menu(stock, "Thẻ kho", "/admin/inventory-ledger", PermissionCodes.Inventory.Transaction.View, 3, "bx bx-transfer-alt");
            var report = Menu(management, "Báo cáo bán hàng", "/admin/reports/overview", PermissionCodes.Report.Sales.View, 1, "bx bx-bar-chart-alt-2");
            var hidden = Menu(management, "HOME_HIDDEN_CONFIG_CANARY", "/admin/reports/sales", PermissionCodes.Report.Sales.View, 2, "bx bx-hide");
            var inactive = Menu(stock, "HOME_INACTIVE_CANARY", "/admin/products", PermissionCodes.Catalog.Product.View, 4, "bx bx-box"); inactive.IsActive = false;
            db.AddRange(pos, orders, customers, products, receiving, ledger, report, hidden, inactive);
            db.Add(new AdminMenuItem { StoreId = foreign.StoreId, Title = "HOME_OTHER_STORE_CANARY", Url = "/admin/reports/overview", PermissionCode = PermissionCodes.Report.Sales.View });
            await db.SaveChangesAsync();
            db.Add(new AdminMenuVisibilitySetting { StoreId = shop.StoreId, RoleId = manager.Role.Id, HiddenMenuIdsJson = JsonSerializer.Serialize(new[] { hidden.Id }) });
            db.Add(new AdminMenuPinSetting { StoreId = shop.StoreId, PinnedMenuIdsJson = JsonSerializer.Serialize(new[] { receiving.Id, pos.Id, products.Id, report.Id }) });
            async Task SeedSale(int storeId, int warehouseId, int variantId, int terminal, int userId, decimal total, string code)
            {
                var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == variantId);
                var unitId = variant.Product.BaseUnitId;
                var conversion = new ProductUnitConversion { StoreId = storeId, ProductVariantId = variantId, UnitId = unitId, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 20 };
                var shift = new POSShift { StoreId = storeId, TerminalId = terminal, WarehouseId = warehouseId,
                    OpenedByUserId = userId, ShiftCode = code, Status = POSShiftStatus.Open, OpenedAtUtc = DateTime.UtcNow };
                db.AddRange(conversion, shift); await db.SaveChangesAsync();
                var order = new Order { StoreId = storeId, POSShiftId = shift.Id, OrderNumber = code, Status = OrderStatus.Completed,
                    PaymentStatus = PaymentStatus.Paid, Subtotal = total, GrandTotal = total, PaidTotal = total, CompletedAtUtc = DateTime.UtcNow, CreatedBy = userId };
                db.Add(new OrderLine { StoreId = storeId, Order = order, ProductId = variant.ProductId, VariantId = variant.Id,
                    ProductUnitConversionId = conversion.Id, SellingUnitId = unitId, BaseUnitId = unitId, ItemName = "Sữa tươi hữu cơ", BaseUnitName = "Chai",
                    SellingUnitName = "Chai", Quantity = total / 20, BaseQuantity = total / 20, Multiplier = 1, UnitPrice = 20, LineTotal = total });
                await db.SaveChangesAsync();
            }
            await SeedSale(shop.StoreId, shop.WarehouseId, shop.ProductVariantId, terminal.Id, manager.User.Id, 100, "HOME-SALE");
            await SeedSale(foreign.StoreId, foreign.WarehouseId, foreign.ProductVariantId, otherTerminal.Id, foreignOwner.User.Id, 900000, "OTHER-SALE");
        }
        async Task<string> State()
        {
            await using var db = app.Database.CreateHostContext();
            async Task<object> Rows<T>() where T : class {
                var rows = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync();
                return rows.Select(row => db.Entry(row).Properties.OrderBy(p => p.Metadata.Name).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)).ToArray();
            }
            return JsonSerializer.Serialize(new { stores = await Rows<Store>(), menus = await Rows<AdminMenuItem>(), pins = await Rows<AdminMenuPinSetting>(),
                visibility = await Rows<AdminMenuVisibilitySetting>(), roles = await Rows<Role>(), grants = await Rows<RolePermission>(),
                orders = await Rows<Order>(), lines = await Rows<OrderLine>(), payments = await Rows<OrderPayment>(), stock = await Rows<InventoryBalance>(),
                rewards = await Rows<RewardSettings>(), vouchers = await Rows<CustomerRewardVoucher>() });
        }
        var before = await State();
        using var client = app.CreateClient(); await app.StartWebAsync(client);
        var folder = Path.Combine(root, "TestResults", "desktop-home-current-20261029"); Directory.CreateDirectory(folder);
        await Browser("baseline");
        await app.SwitchReleaseAsync(candidate, client);
        await Browser("candidate");
        Assert.Equal(before, await State());
        using var review = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "candidate-browser.json")));
        var pixels = review.RootElement.GetProperty("phonePixelReview");
        Assert.InRange(pixels.GetProperty("changedPixels").GetInt32(), 0, pixels.GetProperty("maximumChangedPixels").GetInt32());
        await using (var db = app.Database.CreateHostContext()) { Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); }
        await File.WriteAllTextAsync(Path.Combine(folder, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, phoneLayoutPixelVerified = true, phoneChangedPixels = pixels.GetProperty("changedPixels").GetInt32(),
            phoneMaximumChangedFraction = .0001, actualMigration = true, noPendingMigrations = true, sales = 100, foreignSales = 900000,
            storesMenusPinsVisibilityRolesGrantsOrdersPaymentsStockRewardsVouchersUnchanged = true,
            noRealHostOrStoreUsed = true, ChromeExistingWindowsPreserved = true,
            stateSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(before)))
        }, new JsonSerializerOptions { WriteIndented = true }));
        async Task Browser(string phase)
        {
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
            start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "desktop-home.browser.cjs"));
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { phase, baseUrl = app.BrowserAddress.ToString().TrimEnd('/'),
                terminalId, foreignTerminalId, accounts, output = folder })); process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); } catch { if (!process.HasExited) process.Kill(true); throw; }
            var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(folder, phase + ".log"), log);
            Assert.True(process.ExitCode == 0, log);
        }
    }
}

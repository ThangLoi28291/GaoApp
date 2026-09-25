using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public sealed class AdminMenuRecoverySeederTests
{
    [Fact]
    public async Task Profit_report_menu_is_idempotent_and_keeps_custom_rows_untouched()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "profit-menu");
        var custom = new AdminMenuItem { StoreId = store.Id, Title = "Private report", IsSystem = false,
            Url = "/custom/profit", PermissionCode = "custom.profit" };
        db.AdminMenuItems.Add(custom); await db.SaveChangesAsync();
        await AdminMenuSeeder.SeedAsync(db);
        var count = await ActiveSystemMenuCountAsync(db, store.Id);
        await AdminMenuSeeder.SeedAsync(db);
        (await ActiveSystemMenuCountAsync(db, store.Id)).Should().Be(count);
        var menus = await MenusForStoreAsync(db, store.Id);
        var profit = menus.Should().ContainSingle(x => x.IsSystem && x.Controller == "ProfitReport").Subject;
        profit.Url.Should().Be("/admin/reports/profit");
        profit.PermissionCode.Should().Be(PermissionCodes.Report.Profit.View);
        menus.Single(x => x.Id == profit.ParentId).PermissionCode.Should().BeNull();
        custom.Url.Should().Be("/custom/profit"); custom.PermissionCode.Should().Be("custom.profit");
        custom.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_active_store_receives_canonical_practical_menu_tree()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "active-one");

        await AdminMenuSeeder.SeedAsync(db);

        var menus = await MenusForStoreAsync(db, store.Id);
        var inventory = menus.Should().ContainSingle(x => x.Title == "Kho" && x.ParentId == null).Subject;
        var quickReceiving = menus.Should().ContainSingle(x => x.Title == "Nhập hàng nhanh").Subject;
        quickReceiving.ParentId.Should().Be(inventory.Id);
        quickReceiving.Controller.Should().Be("WarehouseReceiving");
        quickReceiving.Action.Should().Be("Index");
        quickReceiving.Url.Should().Be("/admin/warehouse-receiving");
        quickReceiving.PermissionCode.Should().Be(PermissionCodes.Inventory.StockDocument.Create);
        quickReceiving.SortOrder.Should().Be(35);
        quickReceiving.IsSystem.Should().BeTrue();
        quickReceiving.IsActive.Should().BeTrue();
        menus.Should().Contain(x => x.Title == "Tra cứu tồn kho" && x.Url == "/admin/inventory-inquiry");
        menus.Should().Contain(x => x.Title == "Thẻ kho / Lịch sử giao dịch kho" && x.Url == "/admin/inventory-ledger");
        menus.Should().Contain(x => x.Title == "Phiếu điều chỉnh kho" && x.Url == "/admin/inventory-adjustment-documents");
        menus.Should().Contain(x => x.Title == "Xử lý sự cố tồn kho" && x.Url == "/admin/inventory-issues");
        menus.Should().Contain(x =>
            x.Title == "Quản lý kho" &&
            x.Controller == "WarehouseManagement" &&
            x.Action == "Index" &&
            x.PermissionCode == PermissionCodes.Inventory.Warehouse.View);
        menus.Should().Contain(x =>
            x.Title == "Phiếu kiểm kê kho" &&
            x.Url == "/admin/stock-counts" &&
            x.PermissionCode == PermissionCodes.Inventory.StockCount.View);
        menus.Should().Contain(x =>
            x.Title == "Nhà cung cấp" &&
            x.PermissionCode == PermissionCodes.Catalog.Supplier.View);
        menus.Should().Contain(x =>
            x.Title == "Khách hàng" &&
            x.Controller == "Customer" &&
            x.Action == "Index" &&
            x.PermissionCode == PermissionCodes.Catalog.Customer.View);
        menus.Should().Contain(x =>
            x.Title == "Quản lý menu" &&
            x.PermissionCode == PermissionCodes.Security.Role.Permissions);
        menus.Where(x => !x.IsDeleted).Should().OnlyContain(x => x.IsSystem && x.IsActive);
    }

    [Fact]
    public async Task Partial_soft_deleted_and_stale_system_rows_are_repaired()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "repair-one");
        await AdminMenuSeeder.SeedAsync(db);

        var ledger = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Thẻ kho / Lịch sử giao dịch kho");
        db.AdminMenuItems.Remove(ledger);

        var inquiry = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Tra cứu tồn kho");
        inquiry.IsDeleted = true;
        inquiry.DeletedAtUtc = DateTime.UtcNow.AddDays(-1);
        inquiry.DeletedBy = 17;
        inquiry.IsActive = false;

        var warehouse = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Quản lý kho");
        warehouse.Title = "Kho hàng";
        warehouse.Controller = "Warehouse";
        warehouse.Action = "LegacyIndex";
        warehouse.Url = null;

        var stockCount = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Phiếu kiểm kê kho");
        stockCount.Title = "Kiểm kê kho";
        stockCount.Controller = "StockCount";
        stockCount.Action = "Index";
        stockCount.Url = null;

        var supplier = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Nhà cung cấp");
        supplier.PermissionCode = "catalog.supplier.manage";

        var menuAdmin = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Quản lý menu");
        menuAdmin.PermissionCode = "security.role.manage";
        await db.SaveChangesAsync();

        await AdminMenuSeeder.SeedAsync(db);

        var menus = await MenusForStoreAsync(db, store.Id);
        menus.Should().ContainSingle(x => x.Title == "Thẻ kho / Lịch sử giao dịch kho" && !x.IsDeleted);
        menus.Should().ContainSingle(x =>
            x.Title == "Tra cứu tồn kho" &&
            !x.IsDeleted &&
            x.IsActive &&
            x.DeletedAtUtc == null &&
            x.DeletedBy == null);
        menus.Should().ContainSingle(x =>
            x.Title == "Quản lý kho" &&
            x.Controller == "WarehouseManagement" &&
            x.Action == "Index" &&
            x.Url == null);
        menus.Should().ContainSingle(x =>
            x.Title == "Phiếu kiểm kê kho" &&
            x.Url == "/admin/stock-counts" &&
            x.Controller == "StockCountPages" &&
            x.Action == "Index");
        menus.Should().ContainSingle(x =>
            x.Title == "Nhà cung cấp" &&
            x.PermissionCode == PermissionCodes.Catalog.Supplier.View);
        menus.Should().ContainSingle(x =>
            x.Title == "Quản lý menu" &&
            x.PermissionCode == PermissionCodes.Security.Role.Permissions);
    }

    [Fact]
    public async Task Store_with_only_one_known_system_row_restores_the_missing_tree_without_replacing_it()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "partial-tree");
        var existingDashboard = new AdminMenuItem
        {
            StoreId = store.Id,
            Title = "Trang chủ",
            Area = "Admin",
            Controller = "Home",
            Action = "Index",
            PermissionCode = PermissionCodes.Admin.DashboardView,
            SortOrder = 10,
            IsActive = true,
            IsSystem = true
        };
        db.AdminMenuItems.Add(existingDashboard);
        await db.SaveChangesAsync();
        var existingId = existingDashboard.Id;

        await AdminMenuSeeder.SeedAsync(db);

        var menus = await MenusForStoreAsync(db, store.Id);
        menus.Should().ContainSingle(x => x.Title == "Trang chủ" && !x.IsDeleted);
        menus.Single(x => x.Title == "Trang chủ" && !x.IsDeleted).Id.Should().Be(existingId);
        menus.Should().Contain(x => x.Title == "Danh mục" && !x.IsDeleted);
        menus.Should().Contain(x => x.Title == "Kho" && !x.IsDeleted);
        menus.Should().Contain(x => x.Title == "Mua hàng" && !x.IsDeleted);
        menus.Should().Contain(x => x.Title == "Bảo mật & nhân sự" && !x.IsDeleted);
    }

    [Fact]
    public async Task Every_active_store_is_repaired_but_inactive_or_deleted_stores_are_ignored()
    {
        await using var db = CreateContext();
        var activeOne = await AddStoreAsync(db, "active-one");
        var activeTwo = await AddStoreAsync(db, "active-two");
        var inactive = await AddStoreAsync(db, "inactive", isActive: false);
        var deleted = await AddStoreAsync(db, "deleted", isDeleted: true);

        await AdminMenuSeeder.SeedAsync(db);

        (await ActiveSystemMenuCountAsync(db, activeOne.Id)).Should().BeGreaterThan(0);
        (await ActiveSystemMenuCountAsync(db, activeTwo.Id)).Should()
            .Be(await ActiveSystemMenuCountAsync(db, activeOne.Id));
        (await ActiveSystemMenuCountAsync(db, inactive.Id)).Should().Be(0);
        (await ActiveSystemMenuCountAsync(db, deleted.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_sync_is_semantically_idempotent_and_preserves_custom_rows()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "idempotent");
        var customCreated = new DateTime(2026, 8, 1, 1, 2, 3, DateTimeKind.Utc);
        var customRow = new AdminMenuItem
        {
            StoreId = store.Id,
            Title = "Báo cáo nội bộ tùy chỉnh",
            Area = "Admin",
            Controller = "CustomReports",
            Action = "Index",
            PermissionCode = "custom.reports.private",
            SortOrder = 777,
            IsActive = false,
            IsSystem = false,
            CreatedAtUtc = customCreated
        };
        db.AdminMenuItems.Add(customRow);
        await db.SaveChangesAsync();

        await AdminMenuSeeder.SeedAsync(db);
        var first = await SnapshotAsync(db, store.Id);

        await AdminMenuSeeder.SeedAsync(db);
        var second = await SnapshotAsync(db, store.Id);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
        var custom = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && !x.IsSystem);
        custom.Title.Should().Be("Báo cáo nội bộ tùy chỉnh");
        custom.PermissionCode.Should().Be("custom.reports.private");
        custom.IsActive.Should().BeFalse();
        custom.SortOrder.Should().Be(777);
    }

    [Fact]
    public async Task Existing_valid_system_row_is_not_duplicated()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "existing-valid");
        await AdminMenuSeeder.SeedAsync(db);

        var warehouseId = (await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Quản lý kho")).Id;

        await AdminMenuSeeder.SeedAsync(db);

        var rows = await db.AdminMenuItems.IgnoreQueryFilters()
            .Where(x => x.StoreId == store.Id && x.Title == "Quản lý kho" && !x.IsDeleted)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(warehouseId);
    }

    [Fact]
    public async Task Active_and_soft_deleted_system_duplicates_are_collapsed_deterministically()
    {
        await using var db = CreateContext();
        var store = await AddStoreAsync(db, "duplicate");
        await AdminMenuSeeder.SeedAsync(db);

        var canonical = await db.AdminMenuItems.IgnoreQueryFilters()
            .SingleAsync(x => x.StoreId == store.Id && x.Title == "Nhà cung cấp");
        var duplicate = new AdminMenuItem
        {
            StoreId = store.Id,
            ParentId = canonical.ParentId,
            Title = canonical.Title,
            Area = canonical.Area,
            Controller = canonical.Controller,
            Action = canonical.Action,
            Url = canonical.Url,
            Icon = canonical.Icon,
            PermissionCode = "catalog.supplier.manage",
            SortOrder = canonical.SortOrder,
            IsActive = true,
            IsSystem = true,
            IsDeleted = false
        };
        db.AdminMenuItems.Add(duplicate);
        await db.SaveChangesAsync();
        duplicate.IsDeleted = true;
        duplicate.IsActive = false;
        duplicate.DeletedAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        await AdminMenuSeeder.SeedAsync(db);

        var matches = await db.AdminMenuItems.IgnoreQueryFilters()
            .Where(x => x.StoreId == store.Id && x.IsSystem && x.Title == "Nhà cung cấp")
            .OrderBy(x => x.Id)
            .ToListAsync();
        matches.Should().HaveCount(2);
        matches.Should().ContainSingle(x => !x.IsDeleted && x.IsActive);
        matches.Single(x => !x.IsDeleted).Id.Should().Be(canonical.Id);
        matches.Single(x => !x.IsDeleted).PermissionCode.Should()
            .Be(PermissionCodes.Catalog.Supplier.View);
    }

    private static async Task<Store> AddStoreAsync(
        AppDbContext db,
        string subdomain,
        bool isActive = true,
        bool isDeleted = false)
    {
        var store = new Store
        {
            Name = subdomain,
            SubDomain = subdomain,
            SubDomainNormalized = subdomain,
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        if (isDeleted)
        {
            store.IsDeleted = true;
            store.DeletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return store;
    }

    private static Task<List<AdminMenuItem>> MenusForStoreAsync(AppDbContext db, int storeId)
        => db.AdminMenuItems.IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.Id)
            .ToListAsync();

    private static Task<int> ActiveSystemMenuCountAsync(AppDbContext db, int storeId)
        => db.AdminMenuItems.IgnoreQueryFilters()
            .CountAsync(x => x.StoreId == storeId && x.IsSystem && !x.IsDeleted && x.IsActive);

    private static async Task<List<object>> SnapshotAsync(AppDbContext db, int storeId)
        => (await MenusForStoreAsync(db, storeId))
            .Select(x => (object)new
            {
                x.Id,
                x.StoreId,
                x.ParentId,
                x.Title,
                x.Area,
                x.Controller,
                x.Action,
                x.Url,
                x.Icon,
                x.PermissionCode,
                x.SortOrder,
                x.IsActive,
                x.IsSystem,
                x.IsDeleted,
                x.CreatedAtUtc,
                x.CreatedBy,
                x.UpdatedAtUtc,
                x.UpdatedBy,
                x.DeletedAtUtc,
                x.DeletedBy
            })
            .ToList();

    private static InMemoryAppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"menu-recovery-{Guid.NewGuid():N}")
            .Options;
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        return new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 9001;
        public string? UserName => "menu-recovery-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}

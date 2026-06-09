using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Seed;

/// <summary>
/// Seed dữ liệu menu admin mặc định.
/// 
/// Mục tiêu:
/// - Tạo lại toàn bộ menu hiện tại đang hard-code trong _VerticalMenu.cshtml.
/// - Dùng StoreId = 1 vì AdminMenuItem kế thừa BaseStoreEntity nên StoreId là int bắt buộc.
/// - Chỉ seed 1 lần, nếu store đã có menu thì không tạo trùng.
/// </summary>
public static class AdminMenuSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        const int storeId = 1;

        // Nếu store này đã có menu thì không seed lại để tránh trùng dữ liệu.
        var hasData = await db.AdminMenuItems
      .IgnoreQueryFilters()
      .AnyAsync(x =>
          x.StoreId == storeId &&
          x.Title == "Trang chủ",
          ct);

        if (hasData)
            return;

        var now = DateTime.UtcNow;

        // 1. Trang chủ
        var dashboard = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Trang chủ",
            Area = "Admin",
            Controller = "Home",
            Action = "Index",
            Url = null,
            Icon = "bx bx-home-smile",
            PermissionCode = AppPermissions.AdminDashboardView,
            SortOrder = 10,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        // 2. Nhóm Danh mục
        var catalog = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Danh mục",
            Area = "Admin",
            Controller = null,
            Action = null,
            Url = null,
            Icon = "bx bx-box",
            PermissionCode = null,
            SortOrder = 20,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Sản phẩm",
            Area = "Admin",
            Controller = "Product",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogProductView,
            SortOrder = 21,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Loại sản phẩm",
            Area = "Admin",
            Controller = "Category",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogCategoryManage,
            SortOrder = 22,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Đơn vị tính",
            Area = "Admin",
            Controller = "Unit",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogUnitManage,
            SortOrder = 23,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Nhà cung cấp",
            Area = "Admin",
            Controller = "Supplier",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogSupplierManage,
            SortOrder = 24,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Thuộc tính",
            Area = "Admin",
            Controller = "ProductAttribute",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogProductView,
            SortOrder = 25,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        catalog.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "GT thuộc tính",
            Area = "Admin",
            Controller = "AttributeValue",
            Action = "Index",
            PermissionCode = AppPermissions.CatalogProductView,
            SortOrder = 26,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        // 3. Nhóm Kho
        var inventory = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Kho",
            Area = "Admin",
            Controller = null,
            Action = null,
            Url = null,
            Icon = "bx bx-buildings",
            PermissionCode = null,
            SortOrder = 30,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        inventory.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Kho hàng",
            Area = "Admin",
            Controller = "Warehouse",
            Action = "Index",
            PermissionCode = AppPermissions.InventoryWarehouseView,
            SortOrder = 31,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        inventory.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Phiếu nhập kho",
            Area = "Admin",
            Controller = "StockDocumentManagement",
            Action = "Index",
            PermissionCode = AppPermissions.InventoryStockDocumentView,
            SortOrder = 32,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        inventory.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Chuyển kho",
            Area = "Admin",
            Controller = "StockTransfer",
            Action = "Index",
            PermissionCode = AppPermissions.InventoryStockTransferView,
            SortOrder = 33,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        inventory.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Kiểm kê kho",
            Area = "Admin",
            Controller = "StockCount",
            Action = "Index",
            PermissionCode = AppPermissions.InventoryStockCountView,
            SortOrder = 34,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        // 4. Bán hàng POS
        var pos = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Bán hàng POS",
            Area = "Admin",
            Controller = "POS",
            Action = "Index",
            Url = null,
            Icon = "bx bx-cart",
            PermissionCode = AppPermissions.PosSell,
            SortOrder = 40,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        // 5. Màn hình khách
        var display = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Màn hình khách",
            Area = "Admin",
            Controller = "DisplayPromotion",
            Action = "Index",
            Url = null,
            Icon = "bx bx-slideshow",
            PermissionCode = AppPermissions.PosSell,
            SortOrder = 50,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        // 6. Nhóm Bảo mật & nhân sự
        var security = new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Bảo mật & nhân sự",
            Area = "Admin",
            Controller = null,
            Action = null,
            Url = null,
            Icon = "bx bx-shield-quarter",
            PermissionCode = null,
            SortOrder = 90,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        };

        security.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Vai trò",
            Area = "Admin",
            Controller = "Roles",
            Action = "Index",
            PermissionCode = AppPermissions.SecurityRoleManage,
            SortOrder = 91,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        security.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Nhân viên",
            Area = "Admin",
            Controller = "StoreEmployees",
            Action = "Index",
            PermissionCode = AppPermissions.SecurityEmployeeManage,
            SortOrder = 92,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        security.Children.Add(new AdminMenuItem
        {
            StoreId = storeId,
            Title = "Quản lý menu",
            Area = "Admin",
            Controller = "AdminMenus",
            Action = "Index",
            PermissionCode = AppPermissions.SecurityRoleManage,
            SortOrder = 93,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        });

        /*
         * Dùng AddRange thay vì AddRangeAsync dạng nhiều tham số.
         * Nếu dùng:
         * AddRangeAsync(dashboard, catalog, ..., ct)
         * EF sẽ hiểu ct là 1 entity AdminMenuItem và báo lỗi.
         */
        db.AdminMenuItems.AddRange(
            dashboard,
            catalog,
            inventory,
            pos,
            display,
            security);

        await db.SaveChangesAsync(ct);
    }
}
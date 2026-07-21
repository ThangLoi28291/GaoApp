using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Seed;

/// <summary>
/// Seed dữ liệu security theo permission convention chuẩn mới.
/// 
/// Bao gồm:
/// - Permission global toàn hệ thống
/// - Role mặc định theo từng Store
/// - RolePermission cho từng role
/// - Helper seed user test vào Store
/// 
/// Nguyên tắc:
/// - Permission là global, seed 1 lần
/// - Role là theo Store, seed riêng từng store
/// - Chỉ thêm mới / bổ sung thiếu, không xóa dữ liệu cũ
/// - An toàn để chạy nhiều lần
/// </summary>
public static class SecuritySeedData
{
    /// <summary>
    /// Seed toàn bộ permission chuẩn từ PermissionCatalog.All.
    /// Chỉ thêm mới những permission còn thiếu.
    /// </summary>
    public static async Task SeedPermissionsAsync(AppDbContext context, CancellationToken ct = default)
    {
        var existingCodes = await context.Permissions
            .AsNoTracking()
            .Select(x => x.Code)
            .ToListAsync(ct);

        var existingCodeSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = PermissionCatalog.All
            .Where(x => !existingCodeSet.Contains(x.Code))
            .Select(x => new Permission
            {
                Code = x.Code,
                Name = x.Name,
                GroupName = x.GroupName
            })
            .ToList();

        if (toAdd.Count == 0)
            return;

        await context.Permissions.AddRangeAsync(toAdd, ct);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Đảm bảo mỗi Store có menu hệ thống quản lý HKD. Seeder idempotent và
    /// không sửa/xóa các menu tùy biến hiện hữu của người dùng.
    /// </summary>
    public static async Task SeedLegalEntityAdminMenusAsync(
        AppDbContext context,
        CancellationToken ct = default)
    {
        var storeIds = await context.Stores
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (storeIds.Count == 0)
            return;

        var existingManagementStoreIds = await context.AdminMenuItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.Controller == "LegalEntityManagement")
            .Select(x => x.StoreId)
            .Distinct()
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var menus = storeIds.Except(existingManagementStoreIds).Select(storeId => new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Hộ kinh doanh",
            Area = "Admin",
            Controller = "LegalEntityManagement",
            Action = "Index",
            Icon = "bx bx-briefcase-alt-2",
            PermissionCode = PermissionCodes.System.LegalEntity.View,
            SortOrder = 85,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        }).ToList();

        var existingReconciliationStoreIds = await context.AdminMenuItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.Controller == "LegalEntityReconciliation")
            .Select(x => x.StoreId)
            .Distinct()
            .ToListAsync(ct);

        menus.AddRange(storeIds.Except(existingReconciliationStoreIds).Select(storeId => new AdminMenuItem
        {
            StoreId = storeId,
            ParentId = null,
            Title = "Đối soát HKD",
            Area = "Admin",
            Controller = "LegalEntityReconciliation",
            Action = "Index",
            Icon = "bx bx-git-compare",
            PermissionCode = PermissionCodes.System.LegalEntity.Reconcile,
            SortOrder = 86,
            IsActive = true,
            IsSystem = true,
            CreatedAtUtc = now
        }));

        if (menus.Count == 0)
            return;

        await context.AdminMenuItems.AddRangeAsync(menus, ct);
        await context.SaveChangesAsync(ct);
    }

    public static async Task SeedPurchaseAdminMenusAsync(
        AppDbContext context,
        CancellationToken ct = default)
    {
        var storeIds = await context.Stores
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (storeIds.Count == 0)
            return;

        var existingMenus = await context.AdminMenuItems
            .IgnoreQueryFilters()
            .Where(x => storeIds.Contains(x.StoreId) && !x.IsDeleted)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var storeId in storeIds)
        {
            var storeMenus = existingMenus
                .Where(x => x.StoreId == storeId)
                .ToList();

            var orderMenu = storeMenus.FirstOrDefault(x =>
                string.Equals(x.Controller, "PurchaseOrders", StringComparison.OrdinalIgnoreCase));

            var parent = orderMenu?.ParentId is int parentId
                ? storeMenus.FirstOrDefault(x => x.Id == parentId)
                : null;

            parent ??= storeMenus.FirstOrDefault(x =>
                x.ParentId == null &&
                x.IsSystem &&
                string.Equals(x.Title, "Mua hàng", StringComparison.OrdinalIgnoreCase));

            if (parent == null)
            {
                parent = new AdminMenuItem
                {
                    StoreId = storeId,
                    Title = "Mua hàng",
                    Icon = "bx bx-purchase-tag",
                    SortOrder = 35,
                    IsActive = true,
                    IsSystem = true,
                    CreatedAtUtc = now
                };
                context.AdminMenuItems.Add(parent);
            }

            if (!storeMenus.Any(x => string.Equals(
                    x.Controller,
                    "PurchaseRequests",
                    StringComparison.OrdinalIgnoreCase)))
            {
                parent.Children.Add(new AdminMenuItem
                {
                    StoreId = storeId,
                    Title = "Yêu cầu mua hàng",
                    Area = "Admin",
                    Controller = "PurchaseRequests",
                    Action = "Index",
                    PermissionCode = PermissionCodes.Purchase.Request.ViewOwn,
                    SortOrder = 350,
                    IsActive = true,
                    IsSystem = true,
                    CreatedAtUtc = now
                });
            }

            if (orderMenu == null)
            {
                parent.Children.Add(new AdminMenuItem
                {
                    StoreId = storeId,
                    Title = "Đơn đặt hàng",
                    Area = "Admin",
                    Controller = "PurchaseOrders",
                    Action = "Index",
                    PermissionCode = PermissionCodes.Purchase.Order.View,
                    SortOrder = 351,
                    IsActive = true,
                    IsSystem = true,
                    CreatedAtUtc = now
                });
            }

            var hasReceiptMenu = storeMenus.Any(x =>
                string.Equals(x.Controller, "StockDocumentManagement", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.PermissionCode, PermissionCodes.Purchase.Receipt.View, StringComparison.OrdinalIgnoreCase));

            if (!hasReceiptMenu)
            {
                parent.Children.Add(new AdminMenuItem
                {
                    StoreId = storeId,
                    Title = "Phiếu nhập mua",
                    Area = "Admin",
                    Controller = "StockDocumentManagement",
                    Action = "Index",
                    PermissionCode = PermissionCodes.Purchase.Receipt.View,
                    SortOrder = 352,
                    IsActive = true,
                    IsSystem = true,
                    CreatedAtUtc = now
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seed đủ role mặc định cho 1 store.
    /// Có thể gọi khi:
    /// - tạo store mới
    /// - migrate môi trường cũ
    /// - seed demo/dev
    /// </summary>
    public static async Task SeedDefaultRolesForStoreAsync(AppDbContext context, int storeId, CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new ArgumentException("storeId không hợp lệ.", nameof(storeId));

        await SeedPermissionsAsync(context, ct);

        var storeExists = await context.Stores.AnyAsync(x => x.Id == storeId, ct);
        if (!storeExists)
            throw new InvalidOperationException($"StoreId={storeId} không tồn tại.");

        var existingRoleCodes = await context.Roles
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId)
            .Select(x => x.Code)
            .ToListAsync(ct);

        var existingRoleCodeSet = existingRoleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rolesToAdd = new List<Role>();

        if (!existingRoleCodeSet.Contains("ADMIN"))
        {
            rolesToAdd.Add(new Role
            {
                StoreId = storeId,
                Code = "ADMIN",
                Name = "Admin",
                IsSystemRole = true
            });
        }

        if (!existingRoleCodeSet.Contains("MANAGER"))
        {
            rolesToAdd.Add(new Role
            {
                StoreId = storeId,
                Code = "MANAGER",
                Name = "Manager",
                IsSystemRole = true
            });
        }

        if (!existingRoleCodeSet.Contains("CASHIER"))
        {
            rolesToAdd.Add(new Role
            {
                StoreId = storeId,
                Code = "CASHIER",
                Name = "Cashier",
                IsSystemRole = true
            });
        }

        if (!existingRoleCodeSet.Contains("WAREHOUSE"))
        {
            rolesToAdd.Add(new Role
            {
                StoreId = storeId,
                Code = "WAREHOUSE",
                Name = "Warehouse Staff",
                IsSystemRole = true
            });
        }

        if (rolesToAdd.Count > 0)
        {
            await context.Roles.AddRangeAsync(rolesToAdd, ct);
            await context.SaveChangesAsync(ct);
        }

        var permissions = await context.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId)
            .ToListAsync(ct);

        await SyncDefaultRolePermissionsAsync(context, roles, permissions, ct);
    }

    /// <summary>
    /// Seed role mặc định cho tất cả store đang có.
    /// </summary>
    public static async Task SeedDefaultRolesForAllStoresAsync(AppDbContext context, CancellationToken ct = default)
    {
        var storeIds = await context.Stores
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var storeId in storeIds)
        {
            await SeedDefaultRolesForStoreAsync(context, storeId, ct);
        }
    }

    /// <summary>
    /// Seed user vào store với 1 role cụ thể.
    /// Dùng cho môi trường test/dev hoặc khởi tạo dữ liệu demo.
    /// </summary>
    public static async Task SeedUserInStoreAsync(
        AppDbContext context,
        int storeId,
        int userId,
        string roleCode,
        bool isActive = true,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new ArgumentException("storeId không hợp lệ.", nameof(storeId));

        if (userId <= 0)
            throw new ArgumentException("userId không hợp lệ.", nameof(userId));

        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ArgumentException("roleCode không được để trống.", nameof(roleCode));

        await SeedDefaultRolesForStoreAsync(context, storeId, ct);

        var role = await context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Code == roleCode, ct);

        if (role == null)
            throw new InvalidOperationException($"Không tìm thấy role '{roleCode}' trong store {storeId}.");

        var existing = await context.UserInStores
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.UserId == userId, ct);

        if (existing == null)
        {
            var userInStore = new UserInStore
            {
                StoreId = storeId,
                UserId = userId,
                RoleId = role.Id,
                IsActive = isActive
            };

            await context.UserInStores.AddAsync(userInStore, ct);
        }
        else
        {
            existing.RoleId = role.Id;
            existing.IsActive = isActive;
            context.UserInStores.Update(existing);
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Đồng bộ RolePermission mặc định cho các role hệ thống.
    /// Chiến lược:
    /// - chỉ thêm quyền còn thiếu
    /// - không xóa quyền cũ đang có
    /// 
    /// Lý do:
    /// - an toàn production
    /// - tránh mất quyền đã chỉnh tay
    /// </summary>
    private static async Task SyncDefaultRolePermissionsAsync(
        AppDbContext context,
        List<Role> roles,
        List<Permission> permissions,
        CancellationToken ct)
    {
        var roleIds = roles.Select(x => x.Id).ToList();

        var existingRolePermissions = await context.RolePermissions
            .IgnoreQueryFilters()
            .Where(x => roleIds.Contains(x.RoleId))
            .ToListAsync(ct);

        var adminRole = roles.FirstOrDefault(x => x.Code == "ADMIN");
        var managerRole = roles.FirstOrDefault(x => x.Code == "MANAGER");
        var cashierRole = roles.FirstOrDefault(x => x.Code == "CASHIER");
        var warehouseRole = roles.FirstOrDefault(x => x.Code == "WAREHOUSE");

        // ADMIN: có toàn bộ permission chuẩn hiện có.
        if (adminRole != null)
        {
            await EnsureRolePermissionsAsync(
                context,
                adminRole.Id,
                permissions.Select(x => x.Code).ToList(),
                permissions,
                existingRolePermissions,
                ct);
        }

        // MANAGER: quyền điều hành cửa hàng
        if (managerRole != null)
        {
            await EnsureRolePermissionsAsync(
                context,
                managerRole.Id,
                new List<string>
                {
                    PermissionCodes.Admin.DashboardView,

                    PermissionCodes.Catalog.Product.View,
                    PermissionCodes.Catalog.Product.Create,
                    PermissionCodes.Catalog.Product.Update,

                    PermissionCodes.Catalog.Category.View,
                    PermissionCodes.Catalog.Category.Create,
                    PermissionCodes.Catalog.Category.Update,
                    PermissionCodes.Catalog.Category.Delete,

                    PermissionCodes.Catalog.Supplier.View,
                    PermissionCodes.Catalog.Supplier.Create,
                    PermissionCodes.Catalog.Supplier.Update,
                    PermissionCodes.Catalog.Supplier.Delete,

                    PermissionCodes.Catalog.Unit.View,
                    PermissionCodes.Catalog.Unit.Create,
                    PermissionCodes.Catalog.Unit.Update,
                    PermissionCodes.Catalog.Unit.Delete,

                    PermissionCodes.Inventory.Warehouse.View,

                    PermissionCodes.Inventory.StockDocument.View,
                    PermissionCodes.Inventory.StockDocument.Create,
                    PermissionCodes.Inventory.StockDocument.Approve,

                    PermissionCodes.Inventory.StockTransfer.View,
                    PermissionCodes.Inventory.StockTransfer.Create,
                    PermissionCodes.Inventory.StockTransfer.Approve,

                    PermissionCodes.Inventory.StockCount.View,
                    PermissionCodes.Inventory.StockCount.Create,
                    PermissionCodes.Inventory.StockCount.Approve,

                    PermissionCodes.Inventory.Adjustment.View,
                    PermissionCodes.Inventory.Adjustment.Create,
                    PermissionCodes.Inventory.Adjustment.Approve,

                    PermissionCodes.Purchase.Request.ViewOwn,
                    PermissionCodes.Purchase.Request.ViewStore,
                    PermissionCodes.Purchase.Request.Create,
                    PermissionCodes.Purchase.Request.UpdateOwn,
                    PermissionCodes.Purchase.Request.Submit,
                    PermissionCodes.Purchase.Request.Review,
                    PermissionCodes.Purchase.Request.Convert,
                    PermissionCodes.Purchase.Request.ViewCost,
                    PermissionCodes.Purchase.Order.View,
                    PermissionCodes.Purchase.Order.ViewCost,
                    PermissionCodes.Purchase.Order.Create,
                    PermissionCodes.Purchase.Order.Update,
                    PermissionCodes.Purchase.Order.Approve,
                    PermissionCodes.Purchase.Order.Close,
                    PermissionCodes.Purchase.Order.Cancel,
                    PermissionCodes.Purchase.Order.Print,
                    PermissionCodes.Purchase.Receipt.View,
                    PermissionCodes.Purchase.Receipt.Create,
                    PermissionCodes.Purchase.Receipt.Update,
                    PermissionCodes.Purchase.Receipt.Approve,
                    PermissionCodes.Purchase.Receipt.Cancel,

                    PermissionCodes.Pos.Order.View,

                    PermissionCodes.System.LegalEntity.Reconcile,

                    PermissionCodes.Report.Sales.View,
                    PermissionCodes.Report.Inventory.View
                },
                permissions,
                existingRolePermissions,
                ct);
        }

        // CASHIER: tập trung POS
        if (cashierRole != null)
        {
            await EnsureRolePermissionsAsync(
                context,
                cashierRole.Id,
                new List<string>
                {
                    PermissionCodes.Admin.DashboardView,

                    PermissionCodes.Pos.Order.View,
                    PermissionCodes.Pos.Order.Create,
                    PermissionCodes.Pos.Order.Hold,
                    PermissionCodes.Pos.Order.Discount,
                    PermissionCodes.Pos.Order.Finalize
                },
                permissions,
                existingRolePermissions,
                ct);
        }

        // WAREHOUSE: tập trung nghiệp vụ kho
        if (warehouseRole != null)
        {
            await EnsureRolePermissionsAsync(
                context,
                warehouseRole.Id,
                new List<string>
                {
                    PermissionCodes.Inventory.Warehouse.View,

                    PermissionCodes.Inventory.StockDocument.View,
                    PermissionCodes.Inventory.StockDocument.Create,

                    PermissionCodes.Inventory.StockTransfer.View,
                    PermissionCodes.Inventory.StockTransfer.Create,

                    PermissionCodes.Inventory.StockCount.View,
                    PermissionCodes.Inventory.StockCount.Create,

                    PermissionCodes.Inventory.Adjustment.View,
                    PermissionCodes.Inventory.Adjustment.Create,

                    PermissionCodes.Purchase.Request.ViewOwn,
                    PermissionCodes.Purchase.Request.Create,
                    PermissionCodes.Purchase.Request.UpdateOwn,
                    PermissionCodes.Purchase.Request.Submit,
                    PermissionCodes.Purchase.Order.View,
                    PermissionCodes.Purchase.Receipt.View,
                    PermissionCodes.Purchase.Receipt.Create,
                    PermissionCodes.Purchase.Receipt.Update,

                    PermissionCodes.Report.Inventory.View
                },
                permissions,
                existingRolePermissions,
                ct);
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Đảm bảo role có đủ các permission được yêu cầu.
    /// Chỉ thêm mới, không xóa cái cũ.
    /// </summary>
    private static async Task EnsureRolePermissionsAsync(
        AppDbContext context,
        int roleId,
        List<string> permissionCodes,
        List<Permission> permissions,
        List<RolePermission> existingRolePermissions,
        CancellationToken ct)
    {
        var permissionIds = permissions
            .Where(x => permissionCodes.Contains(x.Code, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToHashSet();

        var existingPermissionIds = existingRolePermissions
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .ToHashSet();

        var toAdd = permissionIds
            .Where(permissionId => !existingPermissionIds.Contains(permissionId))
            .Select(permissionId => new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            await context.RolePermissions.AddRangeAsync(toAdd, ct);
        }
    }

    /// <summary>
    /// Seed user demo/test nếu hệ thống chưa có user nào.
    /// </summary>
    public static async Task SeedUsersAsync(
        AppDbContext context,
        string demoPassword,
        CancellationToken ct = default)
    {
        if (await context.Users.AnyAsync(ct))
            return;

        if (string.IsNullOrWhiteSpace(demoPassword) || demoPassword.Length < 12)
        {
            throw new InvalidOperationException(
                "Demo seed yêu cầu SeedData:DemoUserPassword có ít nhất 12 ký tự.");
        }

        var users = new List<User>
        {
            new User
            {
                UserName = "admin",
                FullName = "Quản trị hệ thống",
                Email = "admin@gaomart.vn",
                PasswordHash = PasswordHasherHelper.Hash(demoPassword),
                IsActive = true
            },
            new User
            {
                UserName = "manager",
                FullName = "Quản lý cửa hàng",
                Email = "manager@gaomart.vn",
                PasswordHash = PasswordHasherHelper.Hash(demoPassword),
                IsActive = true
            },
            new User
            {
                UserName = "warehouse",
                FullName = "Nhân viên kho",
                Email = "warehouse@gaomart.vn",
                PasswordHash = PasswordHasherHelper.Hash(demoPassword),
                IsActive = true
            },
            new User
            {
                UserName = "cashier",
                FullName = "Thu ngân",
                Email = "cashier@gaomart.vn",
                PasswordHash = PasswordHasherHelper.Hash(demoPassword),
                IsActive = true
            }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seed mapping user demo vào store với các role mặc định.
    /// </summary>
    public static async Task SeedUserInStoresAsync(
        AppDbContext context,
        int storeId,
        CancellationToken ct = default)
    {
        var adminRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstAsync(x => x.StoreId == storeId && x.Code == "ADMIN", ct);

        var managerRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstAsync(x => x.StoreId == storeId && x.Code == "MANAGER", ct);

        var warehouseRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstAsync(x => x.StoreId == storeId && x.Code == "WAREHOUSE", ct);

        var cashierRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstAsync(x => x.StoreId == storeId && x.Code == "CASHIER", ct);

        var users = await context.Users.ToListAsync(ct);

        var admin = users.First(x => x.UserName == "admin");
        var manager = users.First(x => x.UserName == "manager");
        var warehouse = users.First(x => x.UserName == "warehouse");
        var cashier = users.First(x => x.UserName == "cashier");

        var existingMappings = await context.UserInStores
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId)
            .ToListAsync(ct);

        var mappingsToAdd = new List<UserInStore>();

        if (!existingMappings.Any(x => x.StoreId == storeId && x.UserId == admin.Id))
        {
            mappingsToAdd.Add(new UserInStore
            {
                StoreId = storeId,
                UserId = admin.Id,
                RoleId = adminRole.Id,
                IsActive = true
            });
        }

        if (!existingMappings.Any(x => x.StoreId == storeId && x.UserId == manager.Id))
        {
            mappingsToAdd.Add(new UserInStore
            {
                StoreId = storeId,
                UserId = manager.Id,
                RoleId = managerRole.Id,
                IsActive = true
            });
        }

        if (!existingMappings.Any(x => x.StoreId == storeId && x.UserId == warehouse.Id))
        {
            mappingsToAdd.Add(new UserInStore
            {
                StoreId = storeId,
                UserId = warehouse.Id,
                RoleId = warehouseRole.Id,
                IsActive = true
            });
        }

        if (!existingMappings.Any(x => x.StoreId == storeId && x.UserId == cashier.Id))
        {
            mappingsToAdd.Add(new UserInStore
            {
                StoreId = storeId,
                UserId = cashier.Id,
                RoleId = cashierRole.Id,
                IsActive = true
            });
        }

        if (mappingsToAdd.Count > 0)
        {
            context.UserInStores.AddRange(mappingsToAdd);
            await context.SaveChangesAsync(ct);
        }
    }
}

namespace GaoApp.Application.Common.Security;

/// <summary>
/// Mapping permission chuẩn mới -> các mã permission cũ / mã compatibility.
/// Dùng trong giai đoạn migrate để không làm hỏng hệ thống đang chạy.
/// Sau khi migrate sạch có thể bỏ dần.
/// </summary>
public static class PermissionAliasMap
{
    /// <summary>
    /// Trả ra danh sách mã permission được xem là tương đương với permission chuẩn.
    /// Luôn include chính canonical code trước tiên.
    /// </summary>
    public static IReadOnlyList<string> GetAcceptedCodes(string canonicalCode)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            canonicalCode
        };

        if (_map.TryGetValue(canonicalCode, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    set.Add(alias.Trim());
            }
        }

        // Rule compatibility tổng quát:
        // nếu là *.view / *.create / *.update / *.delete
        // thì có thể chấp nhận thêm quyền *.manage cùng module/entity.
        var manageCode = TryBuildManageCode(canonicalCode);
        if (!string.IsNullOrWhiteSpace(manageCode))
        {
            set.Add(manageCode!);
        }

        return set.ToList();
    }

    private static string? TryBuildManageCode(string canonicalCode)
    {
        var parts = canonicalCode.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;

        var action = parts[2];
        if (action is "view" or "create" or "update" or "delete")
        {
            return $"{parts[0]}.{parts[1]}.manage";
        }

        return null;
    }

    /// <summary>
    /// Mapping tường minh cho các permission cũ đang tồn tại trong hệ thống.
    /// Anh có thể bổ sung dần khi phát hiện thêm legacy code.
    /// </summary>
    private static readonly Dictionary<string, string[]> _map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Category
            [PermissionCodes.Catalog.Category.View] = new[] { "Category.View", "catalog.category.manage" },
            [PermissionCodes.Catalog.Category.Create] = new[] { "Category.Create", "catalog.category.manage" },
            [PermissionCodes.Catalog.Category.Update] = new[] { "Category.Edit", "Category.Update", "catalog.category.manage" },
            [PermissionCodes.Catalog.Category.Delete] = new[] { "Category.Delete", "catalog.category.manage" },

            // Product
            [PermissionCodes.Catalog.Product.View] = new[] { "Product.View", "catalog.product.manage" },
            [PermissionCodes.Catalog.Product.Create] = new[] { "Product.Create", "catalog.product.manage" },
            [PermissionCodes.Catalog.Product.Update] = new[] { "Product.Edit", "Product.Update", "catalog.product.manage" },
            [PermissionCodes.Catalog.Product.Delete] = new[] { "Product.Delete", "catalog.product.manage" },

            // Supplier
            [PermissionCodes.Catalog.Supplier.View] = new[] { "Supplier.View", "catalog.supplier.manage" },
            [PermissionCodes.Catalog.Supplier.Create] = new[] { "Supplier.Create", "catalog.supplier.manage" },
            [PermissionCodes.Catalog.Supplier.Update] = new[] { "Supplier.Edit", "Supplier.Update", "catalog.supplier.manage" },
            [PermissionCodes.Catalog.Supplier.Delete] = new[] { "Supplier.Delete", "catalog.supplier.manage" },

            // Security Role
            [PermissionCodes.Security.Role.View] = new[] { "Role.View", "security.role.manage" },
            [PermissionCodes.Security.Role.Create] = new[] { "Role.Create", "security.role.manage" },
            [PermissionCodes.Security.Role.Update] = new[] { "Role.Edit", "Role.Update", "security.role.manage" },
            [PermissionCodes.Security.Role.Delete] = new[] { "Role.Delete", "security.role.manage" },
            [PermissionCodes.Security.Role.AssignPermission] = new[] { "Role.AssignPermission", "security.role.manage" },

            // Security User
            [PermissionCodes.Security.User.View] = new[] { "User.View", "security.user.manage" },
            [PermissionCodes.Security.User.Create] = new[] { "User.Create", "security.user.manage" },
            [PermissionCodes.Security.User.Update] = new[] { "User.Edit", "User.Update", "security.user.manage" },
            [PermissionCodes.Security.User.Delete] = new[] { "User.Delete", "security.user.manage" },
            [PermissionCodes.Security.User.AssignStore] = new[] { "User.AssignStore", "security.user.manage" },
            [PermissionCodes.Security.User.AssignRole] = new[] { "User.AssignRole", "security.user.manage" },
            // Inventory Adjustment
            [PermissionCodes.Inventory.Adjustment.View] = new[]
{
    "InventoryAdjustment.View",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Create] = new[]
{
    "InventoryAdjustment.Create",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Update] = new[]
{
    "InventoryAdjustment.Edit",
    "InventoryAdjustment.Update",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Delete] = new[]
{
    "InventoryAdjustment.Delete",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Submit] = new[]
{
    "InventoryAdjustment.Submit",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Approve] = new[]
{
    "InventoryAdjustment.Approve",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Reject] = new[]
{
    "InventoryAdjustment.Reject",
    "inventory.adjustment.manage"
},
            [PermissionCodes.Inventory.Adjustment.Cancel] = new[]
{
    "InventoryAdjustment.Cancel",
    "inventory.adjustment.manage"
},
            // Invoice / AutoInvoice compatibility.
            // Existing Admin/Manager đang có system.integration.manage
            // không bị mất quyền ngay khi source mới được triển khai.
            [PermissionCodes.System.Invoice.ManualIssue] = new[]
{
    PermissionCodes.System.Integration.Manage
},

            [PermissionCodes.System.Invoice.Route] = new[]
{
    PermissionCodes.System.Integration.Manage
},

            [PermissionCodes.System.AutoInvoice.Operate] = new[]
{
    PermissionCodes.System.Integration.Manage
},

            [PermissionCodes.System.AutoInvoice.Settings] = new[]
{
    PermissionCodes.System.Integration.Manage
},
        };
}
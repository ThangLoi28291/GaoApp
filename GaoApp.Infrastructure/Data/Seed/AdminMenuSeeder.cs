using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Seed;

/// <summary>
/// Reconciles the canonical GaoApp system menu for every active Store.
/// Custom rows are never candidates because recovery is bounded to known
/// <see cref="AdminMenuItem.IsSystem"/> definitions.
/// </summary>
public static class AdminMenuSeeder
{
    private static readonly IReadOnlyList<MenuDefinition> Definitions =
    [
        Root("dashboard", "Trang chủ", "bx bx-home-smile", 10,
            controller: "Home", permission: PermissionCodes.Admin.DashboardView),

        Root("catalog", "Danh mục", "bx bx-box", 20),
        Child("catalog.product", "catalog", "Sản phẩm", 21,
            controller: "Product", permission: PermissionCodes.Catalog.Product.View),
        Child("catalog.media", "catalog", "Quản lý hình ảnh", 28,
            controller: "MediaLibrary", url: "/admin/media-library", permission: PermissionCodes.Catalog.Product.View),
        Child("catalog.category", "catalog", "Loại sản phẩm", 22,
            controller: "Category", permission: PermissionCodes.Catalog.Category.View),
        Child("catalog.unit", "catalog", "Đơn vị tính", 23,
            controller: "Unit", permission: PermissionCodes.Catalog.Unit.View),
        Child("catalog.supplier", "catalog", "Nhà cung cấp", 24,
            controller: "Supplier", permission: PermissionCodes.Catalog.Supplier.View),
        Child("catalog.attribute", "catalog", "Thuộc tính", 25,
            controller: "ProductAttribute", permission: PermissionCodes.Catalog.ProductAttribute.View),
        Child("catalog.attribute-value", "catalog", "GT thuộc tính", 26,
            controller: "AttributeValue", permission: PermissionCodes.Catalog.AttributeValue.View),
        Child("catalog.customer", "catalog", "Khách hàng", 27,
            controller: "Customer", permission: PermissionCodes.Catalog.Customer.View),

        Child("catalog.customer-deposit", "catalog", "Đặt cọc khách hàng", 30,
            controller: "CustomerDeposit", url: "/admin/customer-deposit", permission: PermissionCodes.CustomerDeposit.View),
        Child("catalog.customer-debt", "catalog", "Công nợ khách hàng", 29,
            controller: "CustomerDebt", url: "/admin/customer-debt", permission: PermissionCodes.CustomerDebt.View),

        Root("inventory", "Kho", "bx bx-buildings", 30),
        Child("inventory.warehouse", "inventory", "Quản lý kho", 31,
            controller: "WarehouseManagement",
            permission: PermissionCodes.Inventory.Warehouse.View,
            legacyTitles: ["Kho hàng"],
            legacyControllers: ["Warehouse"]),
        Child("inventory.inquiry", "inventory", "Tra cứu tồn kho", 32,
            controller: "InventoryInquiry", url: "/admin/inventory-inquiry"),
        Child("inventory.ledger", "inventory", "Thẻ kho / Lịch sử giao dịch kho", 33,
            controller: "InventoryLedger", url: "/admin/inventory-ledger"),
        Child("inventory.xml-stock", "inventory", "Tồn hàng có hóa đơn XML", 33,
            controller: "InvoiceInputStock", url: "/admin/invoice-input-stock",
            permission: PermissionCodes.Inventory.Transaction.View),
        Child("inventory.receipt", "inventory", "Phiếu nhập kho", 34,
            controller: "StockDocumentManagement",
            url: "/admin/stock-documents",
            permission: PermissionCodes.Inventory.StockDocument.View,
            routeIsShared: true),
        Child("inventory.quick-receiving", "inventory", "Nhập hàng nhanh", 35,
            controller: "WarehouseReceiving",
            url: "/admin/warehouse-receiving",
            permission: PermissionCodes.Inventory.StockDocument.Create),
        Child("inventory.transfer", "inventory", "Chuyển kho", 36,
            controller: "StockTransfer",
            url: "/admin/stock-transfers",
            permission: PermissionCodes.Inventory.StockTransfer.View),
        Child("inventory.count", "inventory", "Phiếu kiểm kê kho", 37,
            controller: "StockCountPages",
            url: "/admin/stock-counts",
            permission: PermissionCodes.Inventory.StockCount.View,
            legacyTitles: ["Kiểm kê kho"],
            legacyControllers: ["StockCount"]),
        Child("inventory.adjustment", "inventory", "Phiếu điều chỉnh kho", 38,
            controller: "InventoryAdjustmentDocuments",
            url: "/admin/inventory-adjustment-documents",
            permission: PermissionCodes.Inventory.Adjustment.View),
        Child("inventory.issue", "inventory", "Xử lý sự cố tồn kho", 39,
            controller: "InventoryIssueManagement", url: "/admin/inventory-issues"),

        Root("purchase", "Mua hàng", "bx bx-purchase-tag", 40),
        Child("purchase.input-invoices", "purchase", "Hóa đơn đầu vào XML", 44,
            controller: "InputInvoiceLibrary", url: "/admin/input-invoices",
            permission: PermissionCodes.Inventory.StockDocument.View),
        Child("purchase.request", "purchase", "Yêu cầu mua hàng", 41,
            controller: "PurchaseRequests",
            url: "/admin/purchase-requests",
            permission: PermissionCodes.Purchase.Request.ViewOwn),
        Child("purchase.order", "purchase", "Đơn đặt hàng", 42,
            controller: "PurchaseOrders",
            url: "/admin/purchase-orders",
            permission: PermissionCodes.Purchase.Order.View),
        Child("purchase.receipt", "purchase", "Phiếu nhập mua", 43,
            controller: "StockDocumentManagement",
            url: "/admin/stock-documents",
            permission: PermissionCodes.Purchase.Receipt.View,
            routeIsShared: true),

        Root("pos", "Bán hàng POS", "bx bx-cart", 50,
            controller: "POS", url: "/admin/pos"),
        Root("display", "Màn hình khách", "bx bx-slideshow", 60,
            controller: "DisplayPromotion"),
        Root("receipt-templates", "Mẫu hóa đơn & máy in", "bx bx-printer", 61,
            controller: "ReceiptTemplates", url: "/admin/receipt-templates", permission: PermissionCodes.Pos.Order.Reprint),
        Root("product-labels", "In tem sản phẩm", "bx bx-barcode", 62,
            controller: "LabelPrinting", url: "/admin/label-printing", permission: PermissionCodes.System.ProductLabel.Print),
        Root("product-label-settings", "Cấu hình in tem", "bx bx-slider-alt", 63,
            controller: "LabelPrintingSettings", url: "/admin/label-printing-settings", permission: PermissionCodes.System.ProductLabel.Manage),
        Root("reports", "Báo cáo", "bx bx-bar-chart-alt-2", 70),
        Child("reports.overview", "reports", "Tổng quan kinh doanh", 71,
            controller: "SalesExecutiveReport",
            url: "/admin/reports/overview",
            permission: PermissionCodes.Report.Sales.View),
        Child("reports.sales", "reports", "Bán hàng", 72,
            controller: "SalesReport",
            url: "/admin/reports/sales",
            permission: PermissionCodes.Report.Sales.View),
        Child("reports.profit", "reports", "Lợi nhuận gộp", 73,
            controller: "ProfitReport",
            url: "/admin/reports/profit",
            permission: PermissionCodes.Report.Profit.View),
        Root("legal-management", "Hộ kinh doanh", "bx bx-briefcase-alt-2", 85,
            controller: "LegalEntityManagement",
            permission: PermissionCodes.System.LegalEntity.View),
        Root("legal-reconciliation", "Đối soát HKD", "bx bx-git-compare", 86,
            controller: "LegalEntityReconciliation",
            permission: PermissionCodes.System.LegalEntity.Reconcile),
        Root("invoice-auto", "Phát hành hóa đơn tự động", "bx bx-receipt", 87,
            controller: "AutoInvoice",
            permission: PermissionCodes.System.Integration.Manage),

        Root("security", "Bảo mật & nhân sự", "bx bx-shield-quarter", 90),
        Child("security.role", "security", "Vai trò", 91,
            controller: "Roles", permission: PermissionCodes.Security.Role.View),
        Child("security.employee", "security", "Nhân viên", 92,
            controller: "UserInStores",
            permission: PermissionCodes.Security.UserInStore.View,
            legacyControllers: ["StoreEmployees"]),
        Child("security.menu", "security", "Quản lý menu", 93,
            controller: "AdminMenus", permission: PermissionCodes.Security.Role.Permissions)
    ];

    public static async Task SeedAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var storeIds = await db.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(store => store.IsActive && !store.IsDeleted)
            .OrderBy(store => store.Id)
            .Select(store => store.Id)
            .ToListAsync(ct);

        foreach (var storeId in storeIds)
        {
            await ReconcileStoreAsync(db, storeId, ct);
        }
    }

    private static async Task ReconcileStoreAsync(
        AppDbContext db,
        int storeId,
        CancellationToken ct)
    {
        var rows = await db.AdminMenuItems
            .IgnoreQueryFilters()
            .Where(row => row.StoreId == storeId && row.IsSystem)
            .OrderBy(row => row.Id)
            .ToListAsync(ct);
        var claimedIds = new HashSet<int>();
        var resolved = new Dictionary<string, AdminMenuItem>(StringComparer.Ordinal);

        foreach (var definition in Definitions.Where(item => item.ParentKey is null))
        {
            resolved[definition.Key] = ReconcileDefinition(
                db, rows, claimedIds, storeId, parentId: null, definition);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        foreach (var definition in Definitions.Where(item => item.ParentKey is not null))
        {
            var parent = resolved[definition.ParentKey!];
            resolved[definition.Key] = ReconcileDefinition(
                db, rows, claimedIds, storeId, parent.Id, definition);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static AdminMenuItem ReconcileDefinition(
        AppDbContext db,
        List<AdminMenuItem> rows,
        HashSet<int> claimedIds,
        int storeId,
        int? parentId,
        MenuDefinition definition)
    {
        var candidates = rows
            .Where(row => !claimedIds.Contains(row.Id))
            .Select(row => new
            {
                Row = row,
                ParentMatches = row.ParentId == parentId,
                TitleMatches = Matches(row.Title, definition.AllTitles),
                RouteMatches = RouteMatches(row, definition),
                IsCanonical = IsCanonical(row, parentId, definition)
            })
            .Where(match =>
                match.TitleMatches ||
                (match.RouteMatches && (!definition.RouteIsShared || match.ParentMatches)))
            .OrderByDescending(match => !match.Row.IsDeleted)
            .ThenByDescending(match => match.IsCanonical)
            .ThenByDescending(match => match.ParentMatches)
            .ThenBy(match => match.Row.Id)
            .ToList();

        AdminMenuItem winner;
        if (candidates.Count == 0)
        {
            winner = new AdminMenuItem
            {
                StoreId = storeId,
                CreatedAtUtc = DateTime.UtcNow
            };
            ApplyCanonical(winner, parentId, definition);
            db.AdminMenuItems.Add(winner);
            rows.Add(winner);
        }
        else
        {
            winner = candidates[0].Row;
            ApplyCanonical(winner, parentId, definition);

            foreach (var duplicate in candidates.Skip(1).Select(match => match.Row))
            {
                if (!duplicate.IsDeleted || duplicate.IsActive)
                {
                    duplicate.IsDeleted = true;
                    duplicate.IsActive = false;
                    duplicate.DeletedAtUtc ??= DateTime.UtcNow;
                    duplicate.DeletedBy = null;
                }
            }
        }

        if (winner.Id > 0)
        {
            claimedIds.Add(winner.Id);
        }

        return winner;
    }

    private static void ApplyCanonical(
        AdminMenuItem row,
        int? parentId,
        MenuDefinition definition)
    {
        row.ParentId = parentId;
        row.Title = definition.Title;
        row.Area = definition.Area;
        row.Controller = definition.Controller;
        row.Action = definition.Action;
        row.Url = definition.Url;
        row.Icon = definition.Icon;
        row.PermissionCode = definition.PermissionCode;
        row.SortOrder = definition.SortOrder;
        row.IsActive = true;
        row.IsSystem = true;
        row.IsDeleted = false;
        row.DeletedAtUtc = null;
        row.DeletedBy = null;
    }

    private static bool IsCanonical(
        AdminMenuItem row,
        int? parentId,
        MenuDefinition definition)
        => row.ParentId == parentId
           && Same(row.Title, definition.Title)
           && Same(row.Area, definition.Area)
           && Same(row.Controller, definition.Controller)
           && Same(row.Action, definition.Action)
           && Same(row.Url, definition.Url)
           && Same(row.Icon, definition.Icon)
           && Same(row.PermissionCode, definition.PermissionCode)
           && row.SortOrder == definition.SortOrder
           && row.IsActive
           && row.IsSystem
           && !row.IsDeleted;

    private static bool RouteMatches(AdminMenuItem row, MenuDefinition definition)
        => (!string.IsNullOrWhiteSpace(row.Controller) &&
            Matches(row.Controller, definition.AllControllers))
           || (!string.IsNullOrWhiteSpace(row.Url) &&
               Matches(row.Url, definition.AllUrls));

    private static bool Matches(string? value, IReadOnlyCollection<string> candidates)
        => value is not null && candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private static MenuDefinition Root(
        string key,
        string title,
        string icon,
        int sortOrder,
        string? controller = null,
        string? url = null,
        string? permission = null,
        string[]? legacyTitles = null,
        string[]? legacyControllers = null)
        => Definition(
            key, parentKey: null, title, icon, sortOrder, controller, url,
            permission, routeIsShared: false, legacyTitles, legacyControllers);

    private static MenuDefinition Child(
        string key,
        string parentKey,
        string title,
        int sortOrder,
        string? controller = null,
        string? url = null,
        string? permission = null,
        bool routeIsShared = false,
        string[]? legacyTitles = null,
        string[]? legacyControllers = null)
        => Definition(
            key, parentKey, title, icon: null, sortOrder, controller, url,
            permission, routeIsShared, legacyTitles, legacyControllers);

    private static MenuDefinition Definition(
        string key,
        string? parentKey,
        string title,
        string? icon,
        int sortOrder,
        string? controller,
        string? url,
        string? permission,
        bool routeIsShared,
        string[]? legacyTitles,
        string[]? legacyControllers)
    {
        var action = controller is null ? null : "Index";
        return new MenuDefinition(
            key,
            parentKey,
            title,
            "Admin",
            controller,
            action,
            url,
            icon,
            permission,
            sortOrder,
            routeIsShared,
            [title, .. legacyTitles ?? []],
            controller is null
                ? legacyControllers ?? []
                : [controller, .. legacyControllers ?? []],
            url is null ? [] : [url]);
    }

    private sealed record MenuDefinition(
        string Key,
        string? ParentKey,
        string Title,
        string? Area,
        string? Controller,
        string? Action,
        string? Url,
        string? Icon,
        string? PermissionCode,
        int SortOrder,
        bool RouteIsShared,
        IReadOnlyCollection<string> AllTitles,
        IReadOnlyCollection<string> AllControllers,
        IReadOnlyCollection<string> AllUrls);
}

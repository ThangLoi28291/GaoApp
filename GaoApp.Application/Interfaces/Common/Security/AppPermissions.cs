namespace GaoApp.Application.Common.Security;

/// <summary>
/// Danh sách permission code dùng toàn hệ thống.
/// Dùng string constant để:
/// - dễ seed DB
/// - dễ map policy
/// - dễ đọc trong code và Razor
/// </summary>
public static class AppPermissions
{
    public const string AdminDashboardView = "admin.dashboard.view";

    public const string CatalogProductView = "catalog.product.view";
    public const string CatalogProductCreate = "catalog.product.create";
    public const string CatalogProductUpdate = "catalog.product.update";
    public const string CatalogProductDelete = "catalog.product.delete";

    public const string CatalogCategoryManage = "catalog.category.manage";
    public const string CatalogSupplierManage = "catalog.supplier.manage";
    public const string CatalogUnitManage = "catalog.unit.manage";

    public const string InventoryWarehouseView = "inventory.warehouse.view";
    public const string InventoryStockDocumentView = "inventory.stockdocument.view";
    public const string InventoryStockDocumentCreate = "inventory.stockdocument.create";
    public const string InventoryStockDocumentApprove = "inventory.stockdocument.approve";

    public const string InventoryStockTransferView = "inventory.stocktransfer.view";
    public const string InventoryStockTransferCreate = "inventory.stocktransfer.create";

    public const string InventoryStockCountView = "inventory.stockcount.view";
    public const string InventoryStockCountCreate = "inventory.stockcount.create";

    public const string InventoryAdjustmentCreate = "inventory.adjustment.create";

    public const string PosSell = "pos.sell";
    public const string PosHold = "pos.hold";
    public const string PosDiscount = "pos.discount";
    public const string PosRefund = "pos.refund";

    public const string OrderView = "order.view";

    public const string ReportSalesView = "report.sales.view";
    public const string ReportInventoryView = "report.inventory.view";

    public const string SecurityRoleManage = "security.role.manage";
    public const string SecurityEmployeeManage = "security.employee.manage";

    public static IReadOnlyList<(string Code, string Name, string GroupName)> All => new List<(string, string, string)>
    {
        (AdminDashboardView, "Xem dashboard", "Dashboard"),

        (CatalogProductView, "Xem sản phẩm", "Catalog"),
        (CatalogProductCreate, "Tạo sản phẩm", "Catalog"),
        (CatalogProductUpdate, "Sửa sản phẩm", "Catalog"),
        (CatalogProductDelete, "Xóa sản phẩm", "Catalog"),
        (CatalogCategoryManage, "Quản lý danh mục", "Catalog"),
        (CatalogSupplierManage, "Quản lý nhà cung cấp", "Catalog"),
        (CatalogUnitManage, "Quản lý đơn vị tính", "Catalog"),

        (InventoryWarehouseView, "Xem kho", "Inventory"),
        (InventoryStockDocumentView, "Xem chứng từ kho", "Inventory"),
        (InventoryStockDocumentCreate, "Tạo chứng từ kho", "Inventory"),
        (InventoryStockDocumentApprove, "Duyệt chứng từ kho", "Inventory"),
        (InventoryStockTransferView, "Xem chuyển kho", "Inventory"),
        (InventoryStockTransferCreate, "Tạo chuyển kho", "Inventory"),
        (InventoryStockCountView, "Xem kiểm kê", "Inventory"),
        (InventoryStockCountCreate, "Tạo kiểm kê", "Inventory"),
        (InventoryAdjustmentCreate, "Điều chỉnh kho", "Inventory"),

        (PosSell, "Bán hàng POS", "POS"),
        (PosHold, "Giữ đơn POS", "POS"),
        (PosDiscount, "Giảm giá POS", "POS"),
        (PosRefund, "Hoàn trả POS", "POS"),

        (OrderView, "Xem đơn hàng", "Orders"),

        (ReportSalesView, "Xem báo cáo bán hàng", "Reports"),
        (ReportInventoryView, "Xem báo cáo tồn kho", "Reports"),

        (SecurityRoleManage, "Quản lý vai trò", "Security"),
        (SecurityEmployeeManage, "Quản lý nhân viên", "Security")
    };
}
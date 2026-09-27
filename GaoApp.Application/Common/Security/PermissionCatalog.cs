using System.Collections.ObjectModel;

namespace GaoApp.Application.Common.Security;

/// <summary>
/// Danh mục permission chuẩn của toàn hệ thống.
/// Đây là "single source of truth" để seed DB và render UI.
/// </summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionRecord> All = new ReadOnlyCollection<PermissionRecord>(
        new List<PermissionRecord>
        {
            P(PermissionCodes.CustomerDeposit.View, "Xem đặt cọc khách hàng", "Customers", "customer", "deposit", "view"),
            P(PermissionCodes.CustomerDeposit.Receive, "Nhận cọc khách hàng", "Customers", "customer", "deposit", "receive"),
            P(PermissionCodes.CustomerDeposit.Use, "Sử dụng cọc tại POS", "Customers", "customer", "deposit", "use"),
            P(PermissionCodes.CustomerDeposit.Refund, "Hoàn cọc khách hàng", "Customers", "customer", "deposit", "refund"),
            P(PermissionCodes.CustomerDebt.View, "Xem công nợ khách hàng", "Customers", "customer", "debt", "view"),
            P(PermissionCodes.CustomerDebt.Sell, "Chốt đơn bán công nợ", "Customers", "customer", "debt", "sell"),
            P(PermissionCodes.CustomerDebt.Collect, "Thu công nợ khách hàng", "Customers", "customer", "debt", "collect"),
            P(PermissionCodes.Admin.DashboardView, "Xem trang tổng quan quản trị", "Admin", "admin", "dashboard", "view"),
            P(PermissionCodes.System.ReceiptTemplate.Manage, "Quản lý mẫu hóa đơn POS", "System", "system", "receipttemplate", "manage"),
            P(PermissionCodes.System.ProductLabel.Manage, "Quản lý mẫu tem và máy in tem", "System", "system", "productlabel", "manage"),
            P(PermissionCodes.System.ProductLabel.Print, "In tem sản phẩm và quản lý phiếu in", "System", "system", "productlabel", "print"),
            P(PermissionCodes.Catalog.Customer.ManageRewards, "Điều chỉnh tích điểm và quản lý voucher", "Catalog", "catalog", "customer", "managerewards"),
            P(PermissionCodes.Catalog.Tax.View, "Xem thuế", "Catalog", "catalog", "tax", "view"),
            P(PermissionCodes.Catalog.Tax.Create, "Tạo thuế", "Catalog", "catalog", "tax", "create"),
            P(PermissionCodes.Catalog.Tax.Update, "Sửa thuế", "Catalog", "catalog", "tax", "update"),
            P(PermissionCodes.Catalog.Tax.Delete, "Xóa thuế", "Catalog", "catalog", "tax", "delete"),
            P(PermissionCodes.Catalog.Promotion.View, "Xem khuyến mãi", "Catalog", "catalog", "promotion", "view"),
            P(PermissionCodes.Catalog.Promotion.Manage, "Quản lý khuyến mãi", "Catalog", "catalog", "promotion", "manage"),
            P(PermissionCodes.Catalog.DisplayPromotion.View, "Xem quảng bá màn hình khách", "Catalog", "catalog", "displaypromotion", "view"),
            P(PermissionCodes.Catalog.DisplayPromotion.Manage, "Quản lý quảng bá màn hình khách", "Catalog", "catalog", "displaypromotion", "manage"),
            P(PermissionCodes.System.BankAccount.View, "Xem tài khoản ngân hàng", "System", "system", "bankaccount", "view"),
            P(PermissionCodes.System.BankAccount.Manage, "Quản lý tài khoản ngân hàng", "System", "system", "bankaccount", "manage"),
            // =========================
            // CATALOG
            // =========================
            P(PermissionCodes.Catalog.Category.View,   "Xem danh mục", "Catalog", "catalog", "category", "view"),
            P(PermissionCodes.Catalog.Category.Create, "Tạo danh mục", "Catalog", "catalog", "category", "create"),
            P(PermissionCodes.Catalog.Category.Update, "Sửa danh mục", "Catalog", "catalog", "category", "update"),
            P(PermissionCodes.Catalog.Category.Delete, "Xóa danh mục", "Catalog", "catalog", "category", "delete"),

            P(PermissionCodes.Catalog.Brand.View,   "Xem thương hiệu", "Catalog", "catalog", "brand", "view"),
            P(PermissionCodes.Catalog.Brand.Create, "Tạo thương hiệu", "Catalog", "catalog", "brand", "create"),
            P(PermissionCodes.Catalog.Brand.Update, "Sửa thương hiệu", "Catalog", "catalog", "brand", "update"),
            P(PermissionCodes.Catalog.Brand.Delete, "Xóa thương hiệu", "Catalog", "catalog", "brand", "delete"),

            P(PermissionCodes.Catalog.Unit.View,   "Xem đơn vị tính", "Catalog", "catalog", "unit", "view"),
            P(PermissionCodes.Catalog.Unit.Create, "Tạo đơn vị tính", "Catalog", "catalog", "unit", "create"),
            P(PermissionCodes.Catalog.Unit.Update, "Sửa đơn vị tính", "Catalog", "catalog", "unit", "update"),
            P(PermissionCodes.Catalog.Unit.Delete, "Xóa đơn vị tính", "Catalog", "catalog", "unit", "delete"),

            P(PermissionCodes.Catalog.Product.View,   "Xem sản phẩm", "Catalog", "catalog", "product", "view"),
            P(PermissionCodes.Catalog.Product.Create, "Tạo sản phẩm", "Catalog", "catalog", "product", "create"),
            P(PermissionCodes.Catalog.Product.Update, "Sửa sản phẩm", "Catalog", "catalog", "product", "update"),
            P(PermissionCodes.Catalog.Product.Delete, "Xóa sản phẩm", "Catalog", "catalog", "product", "delete"),

            P(PermissionCodes.Catalog.ProductVariant.View,   "Xem biến thể sản phẩm", "Catalog", "catalog", "productvariant", "view"),
            P(PermissionCodes.Catalog.ProductVariant.Create, "Tạo biến thể sản phẩm", "Catalog", "catalog", "productvariant", "create"),
            P(PermissionCodes.Catalog.ProductVariant.Update, "Sửa biến thể sản phẩm", "Catalog", "catalog", "productvariant", "update"),
            P(PermissionCodes.Catalog.ProductVariant.Delete, "Xóa biến thể sản phẩm", "Catalog", "catalog", "productvariant", "delete"),

            P(PermissionCodes.Catalog.ProductAttribute.View,   "Xem thuộc tính sản phẩm", "Catalog", "catalog", "productattribute", "view"),
            P(PermissionCodes.Catalog.ProductAttribute.Create, "Tạo thuộc tính sản phẩm", "Catalog", "catalog", "productattribute", "create"),
            P(PermissionCodes.Catalog.ProductAttribute.Update, "Sửa thuộc tính sản phẩm", "Catalog", "catalog", "productattribute", "update"),
            P(PermissionCodes.Catalog.ProductAttribute.Delete, "Xóa thuộc tính sản phẩm", "Catalog", "catalog", "productattribute", "delete"),

            P(PermissionCodes.Catalog.AttributeValue.View,   "Xem giá trị thuộc tính", "Catalog", "catalog", "attributevalue", "view"),
            P(PermissionCodes.Catalog.AttributeValue.Create, "Tạo giá trị thuộc tính", "Catalog", "catalog", "attributevalue", "create"),
            P(PermissionCodes.Catalog.AttributeValue.Update, "Sửa giá trị thuộc tính", "Catalog", "catalog", "attributevalue", "update"),
            P(PermissionCodes.Catalog.AttributeValue.Delete, "Xóa giá trị thuộc tính", "Catalog", "catalog", "attributevalue", "delete"),

            P(PermissionCodes.Catalog.Barcode.View,   "Xem mã vạch", "Catalog", "catalog", "barcode", "view"),
            P(PermissionCodes.Catalog.Barcode.Create, "Tạo mã vạch", "Catalog", "catalog", "barcode", "create"),
            P(PermissionCodes.Catalog.Barcode.Update, "Sửa mã vạch", "Catalog", "catalog", "barcode", "update"),
            P(PermissionCodes.Catalog.Barcode.Delete, "Xóa mã vạch", "Catalog", "catalog", "barcode", "delete"),

            P(PermissionCodes.Catalog.Supplier.View,   "Xem nhà cung cấp", "Catalog", "catalog", "supplier", "view"),
            P(PermissionCodes.Catalog.Supplier.Create, "Tạo nhà cung cấp", "Catalog", "catalog", "supplier", "create"),
            P(PermissionCodes.Catalog.Supplier.Update, "Sửa nhà cung cấp", "Catalog", "catalog", "supplier", "update"),
            P(PermissionCodes.Catalog.Supplier.Delete, "Xóa nhà cung cấp", "Catalog", "catalog", "supplier", "delete"),

            P(PermissionCodes.Catalog.Customer.View,   "Xem khách hàng", "Catalog", "catalog", "customer", "view"),
            P(PermissionCodes.Catalog.Customer.Create, "Tạo khách hàng", "Catalog", "catalog", "customer", "create"),
            P(PermissionCodes.Catalog.Customer.Update, "Sửa khách hàng", "Catalog", "catalog", "customer", "update"),
            P(PermissionCodes.Catalog.Customer.Delete, "Xóa khách hàng", "Catalog", "catalog", "customer", "delete"),

            // =========================
            // INVENTORY
            // =========================
            P(PermissionCodes.Inventory.Warehouse.View,   "Xem kho", "Inventory", "inventory", "warehouse", "view"),
            P(PermissionCodes.Inventory.Warehouse.Create, "Tạo kho", "Inventory", "inventory", "warehouse", "create"),
            P(PermissionCodes.Inventory.Warehouse.Update, "Sửa kho", "Inventory", "inventory", "warehouse", "update"),
            P(PermissionCodes.Inventory.Warehouse.Delete, "Xóa kho", "Inventory", "inventory", "warehouse", "delete"),

            P(PermissionCodes.Inventory.Balance.View,   "Xem tồn kho", "Inventory", "inventory", "balance", "view"),
            P(PermissionCodes.Inventory.Balance.Export, "Xuất tồn kho", "Inventory", "inventory", "balance", "export"),

            P(PermissionCodes.Inventory.Transaction.View,   "Xem giao dịch tồn kho", "Inventory", "inventory", "transaction", "view"),
            P(PermissionCodes.Inventory.Transaction.Export, "Xuất giao dịch tồn kho", "Inventory", "inventory", "transaction", "export"),

            P(PermissionCodes.Inventory.StockDocument.View,    "Xem chứng từ kho", "Inventory", "inventory", "stockdocument", "view"),
            P(PermissionCodes.Inventory.StockDocument.Create,  "Tạo chứng từ kho", "Inventory", "inventory", "stockdocument", "create"),
            P(PermissionCodes.Inventory.StockDocument.Update,  "Sửa chứng từ kho", "Inventory", "inventory", "stockdocument", "update"),
            P(PermissionCodes.Inventory.StockDocument.Delete,  "Xóa chứng từ kho", "Inventory", "inventory", "stockdocument", "delete"),
            P(PermissionCodes.Inventory.StockDocument.Approve, "Duyệt chứng từ kho", "Inventory", "inventory", "stockdocument", "approve"),
            P(PermissionCodes.Inventory.StockDocument.Cancel,  "Hủy chứng từ kho", "Inventory", "inventory", "stockdocument", "cancel"),

           P(PermissionCodes.Inventory.Adjustment.View,    "Xem phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "view"),
P(PermissionCodes.Inventory.Adjustment.Create,  "Tạo phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "create"),
P(PermissionCodes.Inventory.Adjustment.Update,  "Sửa phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "update"),
P(PermissionCodes.Inventory.Adjustment.Delete,  "Xóa phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "delete"),
P(PermissionCodes.Inventory.Adjustment.Submit,  "Gửi duyệt phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "submit"),
P(PermissionCodes.Inventory.Adjustment.Approve, "Duyệt phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "approve"),
P(PermissionCodes.Inventory.Adjustment.Reject,  "Từ chối phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "reject"),
P(PermissionCodes.Inventory.Adjustment.Cancel,  "Hủy phiếu điều chỉnh kho", "Inventory", "inventory", "adjustment", "cancel"),

            P(PermissionCodes.Inventory.StockCount.View,    "Xem kiểm kê", "Inventory", "inventory", "stockcount", "view"),
            P(PermissionCodes.Inventory.StockCount.Create,  "Tạo kiểm kê", "Inventory", "inventory", "stockcount", "create"),
            P(PermissionCodes.Inventory.StockCount.Update,  "Sửa kiểm kê", "Inventory", "inventory", "stockcount", "update"),
            P(PermissionCodes.Inventory.StockCount.Delete,  "Xóa kiểm kê", "Inventory", "inventory", "stockcount", "delete"),
            P(PermissionCodes.Inventory.StockCount.Approve, "Duyệt kiểm kê", "Inventory", "inventory", "stockcount", "approve"),
            P(PermissionCodes.Inventory.StockCount.Close,   "Đóng kiểm kê", "Inventory", "inventory", "stockcount", "close"),

            P(PermissionCodes.Inventory.StockTransfer.View,    "Xem chuyển kho", "Inventory", "inventory", "stocktransfer", "view"),
            P(PermissionCodes.Inventory.StockTransfer.Create,  "Tạo chuyển kho", "Inventory", "inventory", "stocktransfer", "create"),
            P(PermissionCodes.Inventory.StockTransfer.Update,  "Sửa chuyển kho", "Inventory", "inventory", "stocktransfer", "update"),
            P(PermissionCodes.Inventory.StockTransfer.Delete,  "Xóa chuyển kho", "Inventory", "inventory", "stocktransfer", "delete"),
            P(PermissionCodes.Inventory.StockTransfer.Approve, "Duyệt chuyển kho", "Inventory", "inventory", "stocktransfer", "approve"),
            P(PermissionCodes.Inventory.StockTransfer.Confirm, "Xác nhận chuyển kho", "Inventory", "inventory", "stocktransfer", "confirm"),

            P(PermissionCodes.Inventory.Issue.View,      "Xem issue tồn kho", "Inventory", "inventory", "issue", "view"),
            P(PermissionCodes.Inventory.Issue.Update,    "Sửa issue tồn kho", "Inventory", "inventory", "issue", "update"),
            P(PermissionCodes.Inventory.Issue.Approve,   "Duyệt issue tồn kho", "Inventory", "inventory", "issue", "approve"),
            P(PermissionCodes.Inventory.Issue.Reject,    "Từ chối issue tồn kho", "Inventory", "inventory", "issue", "reject"),
            P(PermissionCodes.Inventory.Issue.Reconcile, "Đối soát issue tồn kho", "Inventory", "inventory", "issue", "reconcile"),

            P(PermissionCodes.Inventory.Valuation.View,      "Xem valuation tồn kho", "Inventory", "inventory", "valuation", "view"),
            P(PermissionCodes.Inventory.Valuation.Reconcile, "Đối soát valuation tồn kho", "Inventory", "inventory", "valuation", "reconcile"),
            P(PermissionCodes.Inventory.Valuation.Export,    "Xuất valuation tồn kho", "Inventory", "inventory", "valuation", "export"),

            // =========================
            // PURCHASE
            // =========================
            P(PermissionCodes.Purchase.Request.ViewOwn,   "Xem yêu cầu mua của mình", "Purchase", "purchase", "request", "viewown"),
            P(PermissionCodes.Purchase.Request.ViewStore, "Xem yêu cầu mua toàn cửa hàng", "Purchase", "purchase", "request", "viewstore"),
            P(PermissionCodes.Purchase.Request.Create,    "Tạo yêu cầu mua hàng", "Purchase", "purchase", "request", "create"),
            P(PermissionCodes.Purchase.Request.UpdateOwn, "Sửa yêu cầu mua của mình", "Purchase", "purchase", "request", "updateown"),
            P(PermissionCodes.Purchase.Request.Submit,    "Gửi duyệt yêu cầu mua", "Purchase", "purchase", "request", "submit"),
            P(PermissionCodes.Purchase.Request.Review,    "Duyệt/trả/từ chối yêu cầu mua", "Purchase", "purchase", "request", "review"),
            P(PermissionCodes.Purchase.Request.Convert,   "Tạo đơn đặt hàng từ yêu cầu mua", "Purchase", "purchase", "request", "convert"),
            P(PermissionCodes.Purchase.Request.ViewCost,  "Xem và nhập giá mua/VAT", "Purchase", "purchase", "request", "viewcost"),

            P(PermissionCodes.Purchase.Order.View,    "Xem đơn đặt hàng", "Purchase", "purchase", "order", "view"),
            P(PermissionCodes.Purchase.Order.ViewCost,"Xem giá và VAT trên đơn đặt hàng", "Purchase", "purchase", "order", "viewcost"),
            P(PermissionCodes.Purchase.Order.Create,  "Tạo đơn đặt hàng", "Purchase", "purchase", "order", "create"),
            P(PermissionCodes.Purchase.Order.Update,  "Sửa/gửi đơn đặt hàng", "Purchase", "purchase", "order", "update"),
            P(PermissionCodes.Purchase.Order.Delete,  "Xóa đơn đặt hàng", "Purchase", "purchase", "order", "delete"),
            P(PermissionCodes.Purchase.Order.Approve, "Duyệt/trả đơn đặt hàng", "Purchase", "purchase", "order", "approve"),
            P(PermissionCodes.Purchase.Order.Close,   "Đóng thiếu đơn đặt hàng", "Purchase", "purchase", "order", "close"),
            P(PermissionCodes.Purchase.Order.Cancel,  "Hủy đơn đặt hàng", "Purchase", "purchase", "order", "cancel"),
            P(PermissionCodes.Purchase.Order.Print,   "In hoặc lưu PDF đơn đặt hàng", "Purchase", "purchase", "order", "print"),
            P(PermissionCodes.Purchase.Receipt.View,    "Xem nhận hàng mua", "Purchase", "purchase", "receipt", "view"),
            P(PermissionCodes.Purchase.Receipt.Create,  "Tạo nhận hàng từ đơn", "Purchase", "purchase", "receipt", "create"),
            P(PermissionCodes.Purchase.Receipt.Update,  "Sửa/gửi nhận hàng", "Purchase", "purchase", "receipt", "update"),
            P(PermissionCodes.Purchase.Receipt.Delete,  "Xóa nhận hàng", "Purchase", "purchase", "receipt", "delete"),
            P(PermissionCodes.Purchase.Receipt.Approve, "Duyệt nhận hàng mua", "Purchase", "purchase", "receipt", "approve"),
            P(PermissionCodes.Purchase.Receipt.Cancel,  "Hủy nhận hàng mua", "Purchase", "purchase", "receipt", "cancel"),

            // =========================
            // POS
            // =========================
            P(PermissionCodes.Pos.Order.View,     "Xem đơn POS", "POS", "pos", "order", "view"),
            P(PermissionCodes.Pos.Order.Create,   "Tạo đơn POS", "POS", "pos", "order", "create"),
            P(PermissionCodes.Pos.Order.Finalize, "Hoàn tất đơn POS", "POS", "pos", "order", "finalize"),
            P(PermissionCodes.Pos.Order.Void,     "Hủy đơn POS", "POS", "pos", "order", "void"),
            P(PermissionCodes.Pos.Order.Refund,   "Refund đơn POS", "POS", "pos", "order", "refund"),
            P(PermissionCodes.Pos.Order.Reprint,  "In lại đơn POS", "POS", "pos", "order", "reprint"),
            P(PermissionCodes.Pos.Order.Hold,     "Giữ và lấy lại đơn POS", "POS", "pos", "order", "hold"),
            P(PermissionCodes.Pos.Order.Discount, "Giảm giá đơn POS", "POS", "pos", "order", "discount"),


            P(PermissionCodes.Pos.Shift.View,      "Xem ca POS", "POS", "pos", "shift", "view"),
            P(PermissionCodes.Pos.Shift.Open,      "Mở ca POS", "POS", "pos", "shift", "open"),
            P(PermissionCodes.Pos.Shift.Close,     "Đóng ca POS", "POS", "pos", "shift", "close"),
            P(PermissionCodes.Pos.Shift.Reconcile, "Đối soát ca POS", "POS", "pos", "shift", "reconcile"),
            P(PermissionCodes.Pos.Shift.TakeOver,   "Tiếp quản ca POS", "POS", "pos", "shift", "takeover"),
            P(PermissionCodes.Pos.Shift.ForceClose, "Đóng hộ ca POS",   "POS", "pos", "shift", "forceclose"),

            P(PermissionCodes.Pos.Payment.View,   "Xem thanh toán POS", "POS", "pos", "payment", "view"),
            P(PermissionCodes.Pos.Payment.Create, "Tạo thanh toán POS", "POS", "pos", "payment", "create"),
            P(PermissionCodes.Pos.Payment.Refund, "Refund thanh toán POS", "POS", "pos", "payment", "refund"),

            P(PermissionCodes.Pos.Terminal.View,   "Xem terminal POS", "POS", "pos", "terminal", "view"),
            P(PermissionCodes.Pos.Terminal.Create, "Tạo terminal POS", "POS", "pos", "terminal", "create"),
            P(PermissionCodes.Pos.Terminal.Update, "Sửa terminal POS", "POS", "pos", "terminal", "update"),
            P(PermissionCodes.Pos.Terminal.Delete, "Xóa terminal POS", "POS", "pos", "terminal", "delete"),

            // =========================
            // REPORT
            // =========================
            P(PermissionCodes.Report.Sales.View,   "Xem báo cáo bán hàng", "Reports", "report", "sales", "view"),
            P(PermissionCodes.Report.Sales.Export, "Xuất báo cáo bán hàng", "Reports", "report", "sales", "export"),

            P(PermissionCodes.Report.Inventory.View,   "Xem báo cáo tồn kho", "Reports", "report", "inventory", "view"),
            P(PermissionCodes.Report.Inventory.Export, "Xuất báo cáo tồn kho", "Reports", "report", "inventory", "export"),

            P(PermissionCodes.Report.Profit.View,   "Xem báo cáo lợi nhuận", "Reports", "report", "profit", "view"),
            P(PermissionCodes.Report.Profit.Export, "Xuất báo cáo lợi nhuận", "Reports", "report", "profit", "export"),

            P(PermissionCodes.Report.Purchase.View,   "Xem báo cáo mua hàng", "Reports", "report", "purchase", "view"),
            P(PermissionCodes.Report.Purchase.Export, "Xuất báo cáo mua hàng", "Reports", "report", "purchase", "export"),

            P(PermissionCodes.Report.Shift.View,   "Xem báo cáo ca", "Reports", "report", "shift", "view"),
            P(PermissionCodes.Report.Shift.Export, "Xuất báo cáo ca", "Reports", "report", "shift", "export"),

            P(PermissionCodes.Report.Receivable.View,   "Xem báo cáo công nợ phải thu", "Reports", "report", "receivable", "view"),
            P(PermissionCodes.Report.Receivable.Export, "Xuất báo cáo công nợ phải thu", "Reports", "report", "receivable", "export"),

            P(PermissionCodes.Report.Payable.View,   "Xem báo cáo công nợ phải trả", "Reports", "report", "payable", "view"),
            P(PermissionCodes.Report.Payable.Export, "Xuất báo cáo công nợ phải trả", "Reports", "report", "payable", "export"),

            // =========================
            // SECURITY
            // =========================
            P(PermissionCodes.Security.User.View,        "Xem người dùng", "Security", "security", "user", "view"),
            P(PermissionCodes.Security.User.Create,      "Tạo người dùng", "Security", "security", "user", "create"),
            P(PermissionCodes.Security.User.Update,      "Sửa người dùng", "Security", "security", "user", "update"),
            P(PermissionCodes.Security.User.Delete,      "Xóa người dùng", "Security", "security", "user", "delete"),
            P(PermissionCodes.Security.User.AssignStore, "Gán store cho người dùng", "Security", "security", "user", "assignstore"),
            P(PermissionCodes.Security.User.AssignRole,  "Gán role cho người dùng", "Security", "security", "user", "assignrole"),

            P(PermissionCodes.Security.Role.View,        "Xem role", "Security", "security", "role", "view"),
            P(PermissionCodes.Security.Role.Create,      "Tạo role", "Security", "security", "role", "create"),
            P(PermissionCodes.Security.Role.Update,      "Sửa role", "Security", "security", "role", "update"),
            P(PermissionCodes.Security.Role.Delete,      "Xóa role", "Security", "security", "role", "delete"),
            P(PermissionCodes.Security.Role.Permissions, "Gán permission cho role", "Security", "security", "role", "permissions"),

            P(PermissionCodes.Security.Permission.View,   "Xem permission", "Security", "security", "permission", "view"),
            P(PermissionCodes.Security.Permission.Manage, "Quản lý permission", "Security", "security", "permission", "manage"),

            P(PermissionCodes.Security.UserInStore.View,   "Xem user trong store", "Security", "security", "userinstore", "view"),
            P(PermissionCodes.Security.UserInStore.Create, "Tạo user trong store", "Security", "security", "userinstore", "create"),
            P(PermissionCodes.Security.UserInStore.Update, "Sửa user trong store", "Security", "security", "userinstore", "update"),
            P(PermissionCodes.Security.UserInStore.Delete, "Xóa user trong store", "Security", "security", "userinstore", "delete"),

            // =========================
            // SYSTEM
            // =========================
            P(PermissionCodes.System.Store.View,   "Xem store", "System", "system", "store", "view"),
            P(PermissionCodes.System.Store.Create, "Tạo store", "System", "system", "store", "create"),
            P(PermissionCodes.System.Store.Update, "Sửa store", "System", "system", "store", "update"),
            P(PermissionCodes.System.Store.Delete, "Xóa store", "System", "system", "store", "delete"),

            P(PermissionCodes.System.LegalEntity.View, "Xem HKD", "System", "system", "legalentity", "view"),
            P(PermissionCodes.System.LegalEntity.Create, "Tạo HKD", "System", "system", "legalentity", "create"),
            P(PermissionCodes.System.LegalEntity.Update, "Sửa/khóa HKD", "System", "system", "legalentity", "update"),
            P(PermissionCodes.System.LegalEntity.Activate, "Kiểm tra/kích hoạt Multi LegalEntity", "System", "system", "legalentity", "activate"),
            P(PermissionCodes.System.LegalEntity.Reconcile, "Xem phân bổ và đối soát HKD", "System", "system", "legalentity", "reconcile"),

            P(PermissionCodes.System.Setting.View,   "Xem cấu hình", "System", "system", "setting", "view"),
            P(PermissionCodes.System.Setting.Update, "Sửa cấu hình", "System", "system", "setting", "update"),

            P(PermissionCodes.System.AuditLog.View,   "Xem audit log", "System", "system", "auditlog", "view"),
            P(PermissionCodes.System.AuditLog.Export, "Xuất audit log", "System", "system", "auditlog", "export"),

            P(PermissionCodes.System.Job.View,    "Xem background jobs", "System", "system", "job", "view"),
            P(PermissionCodes.System.Job.Execute, "Chạy background jobs", "System", "system", "job", "execute"),

            P(PermissionCodes.System.Integration.View,   "Xem integration", "System", "system", "integration", "view"),
            P(PermissionCodes.System.Integration.Manage, "Quản lý integration", "System", "system", "integration", "manage"),
            P(
    PermissionCodes.System.Invoice.ManualIssue,
    "Phát hành hóa đơn thủ công",
    "System",
    "system",
    "invoice",
    "manualissue"),

P(
    PermissionCodes.System.Invoice.Route,
    "Chuyển phương thức phát hành hóa đơn",
    "System",
    "system",
    "invoice",
    "route"),

P(
    PermissionCodes.System.AutoInvoice.Operate,
    "Vận hành phát hành hóa đơn tự động",
    "System",
    "system",
    "autoinvoice",
    "operate"),

P(
    PermissionCodes.System.AutoInvoice.Settings,
    "Cấu hình phát hành hóa đơn tự động",
    "System",
    "system",
    "autoinvoice",
    "settings"),
        });

    /// <summary>
    /// Tạo 1 PermissionRecord ngắn gọn hơn.
    /// </summary>
    private static PermissionRecord P(
        string code,
        string name,
        string groupName,
        string module,
        string entity,
        string action,
        string? description = null)
    {
        return new PermissionRecord
        {
            Code = code,
            Name = name,
            GroupName = groupName,
            Module = module,
            Entity = entity,
            Action = action,
            Description = description
        };
    }
}

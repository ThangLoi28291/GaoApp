namespace GaoApp.Application.Common.Security;

/// <summary>
/// Trung tâm định nghĩa toàn bộ permission code chuẩn của hệ thống.
/// Quy ước:
///     &lt;module&gt;.&lt;entity&gt;.&lt;action&gt;
/// Ví dụ:
///     catalog.category.view
///     security.role.permissions
/// </summary>
public static class PermissionCodes
{
    public static class Admin
    {
        public const string DashboardView = "admin.dashboard.view";
    }

    public static class Catalog
    {
        public static class Category
        {
            public const string View = "catalog.category.view";
            public const string Create = "catalog.category.create";
            public const string Update = "catalog.category.update";
            public const string Delete = "catalog.category.delete";
        }

        public static class Brand
        {
            public const string View = "catalog.brand.view";
            public const string Create = "catalog.brand.create";
            public const string Update = "catalog.brand.update";
            public const string Delete = "catalog.brand.delete";
        }

        public static class Unit
        {
            public const string View = "catalog.unit.view";
            public const string Create = "catalog.unit.create";
            public const string Update = "catalog.unit.update";
            public const string Delete = "catalog.unit.delete";
        }

        public static class Product
        {
            public const string View = "catalog.product.view";
            public const string Create = "catalog.product.create";
            public const string Update = "catalog.product.update";
            public const string Delete = "catalog.product.delete";
        }

        /// <summary>
        /// Bổ sung để PermissionCatalog seed được nhóm biến thể sản phẩm.
        /// </summary>
        public static class ProductVariant
        {
            public const string View = "catalog.productvariant.view";
            public const string Create = "catalog.productvariant.create";
            public const string Update = "catalog.productvariant.update";
            public const string Delete = "catalog.productvariant.delete";
        }

        public static class ProductAttribute
        {
            public const string View = "catalog.productattribute.view";
            public const string Create = "catalog.productattribute.create";
            public const string Update = "catalog.productattribute.update";
            public const string Delete = "catalog.productattribute.delete";
        }

        public static class AttributeValue
        {
            public const string View = "catalog.attributevalue.view";
            public const string Create = "catalog.attributevalue.create";
            public const string Update = "catalog.attributevalue.update";
            public const string Delete = "catalog.attributevalue.delete";
        }

        /// <summary>
        /// Bổ sung để PermissionCatalog seed được nhóm mã vạch.
        /// </summary>
        public static class Barcode
        {
            public const string View = "catalog.barcode.view";
            public const string Create = "catalog.barcode.create";
            public const string Update = "catalog.barcode.update";
            public const string Delete = "catalog.barcode.delete";
        }

        public static class Supplier
        {
            public const string View = "catalog.supplier.view";
            public const string Create = "catalog.supplier.create";
            public const string Update = "catalog.supplier.update";
            public const string Delete = "catalog.supplier.delete";
        }

        /// <summary>
        /// Bổ sung để PermissionCatalog seed được nhóm khách hàng.
        /// </summary>
        public static class Customer
        {
            public const string View = "catalog.customer.view";
            public const string Create = "catalog.customer.create";
            public const string Update = "catalog.customer.update";
            public const string Delete = "catalog.customer.delete";
        }
    }

    public static class Inventory
    {
        public static class Warehouse
        {
            public const string View = "inventory.warehouse.view";
            public const string Create = "inventory.warehouse.create";
            public const string Update = "inventory.warehouse.update";
            public const string Delete = "inventory.warehouse.delete";
        }

        public static class StockDocument
        {
            public const string View = "inventory.stockdocument.view";
            public const string Create = "inventory.stockdocument.create";
            public const string Update = "inventory.stockdocument.update";
            public const string Delete = "inventory.stockdocument.delete";
            public const string Approve = "inventory.stockdocument.approve";
            public const string Cancel = "inventory.stockdocument.cancel";
        }

        public static class StockTransfer
        {
            public const string View = "inventory.stocktransfer.view";
            public const string Create = "inventory.stocktransfer.create";
            public const string Update = "inventory.stocktransfer.update";
            public const string Delete = "inventory.stocktransfer.delete";
            public const string Approve = "inventory.stocktransfer.approve";
            public const string Confirm = "inventory.stocktransfer.confirm";
        }

        public static class StockCount
        {
            public const string View = "inventory.stockcount.view";
            public const string Create = "inventory.stockcount.create";
            public const string Update = "inventory.stockcount.update";
            public const string Delete = "inventory.stockcount.delete";
            public const string Approve = "inventory.stockcount.approve";

            /// <summary>
            /// Bổ sung vì PermissionCatalog đang gọi StockCount.Close.
            /// </summary>
            public const string Close = "inventory.stockcount.close";
        }

        public static class Adjustment
        {
            public const string View = "inventory.adjustment.view";
            public const string Create = "inventory.adjustment.create";
            public const string Update = "inventory.adjustment.update";
            public const string Delete = "inventory.adjustment.delete";
            public const string Approve = "inventory.adjustment.approve";
        }

        public static class Balance
        {
            public const string View = "inventory.balance.view";
            public const string Export = "inventory.balance.export";
        }

        public static class Transaction
        {
            public const string View = "inventory.transaction.view";
            public const string Export = "inventory.transaction.export";
        }

        /// <summary>
        /// Nếu PermissionCatalog có nhóm issue/valuation thì để sẵn cho đỡ lỗi tiếp.
        /// </summary>
        public static class Issue
        {
            public const string View = "inventory.issue.view";
            public const string Update = "inventory.issue.update";
            public const string Approve = "inventory.issue.approve";
            public const string Reject = "inventory.issue.reject";
            public const string Reconcile = "inventory.issue.reconcile";
        }

        public static class Valuation
        {
            public const string View = "inventory.valuation.view";
            public const string Reconcile = "inventory.valuation.reconcile";
            public const string Export = "inventory.valuation.export";
        }
    }

    public static class Purchase
    {
        public static class Order
        {
            public const string View = "purchase.order.view";
            public const string Create = "purchase.order.create";
            public const string Update = "purchase.order.update";
            public const string Delete = "purchase.order.delete";
            public const string Approve = "purchase.order.approve";
            public const string Close = "purchase.order.close";
            public const string Cancel = "purchase.order.cancel";
        }

        public static class Receipt
        {
            public const string View = "purchase.receipt.view";
            public const string Create = "purchase.receipt.create";
            public const string Update = "purchase.receipt.update";
            public const string Delete = "purchase.receipt.delete";
            public const string Approve = "purchase.receipt.approve";
            public const string Cancel = "purchase.receipt.cancel";
        }

        public static class Return
        {
            public const string View = "purchase.return.view";
            public const string Create = "purchase.return.create";
            public const string Update = "purchase.return.update";
            public const string Delete = "purchase.return.delete";
            public const string Approve = "purchase.return.approve";
            public const string Cancel = "purchase.return.cancel";
        }
    }

    public static class Sales
    {
        public static class Order
        {
            public const string View = "sales.order.view";
            public const string Create = "sales.order.create";
            public const string Update = "sales.order.update";
            public const string Delete = "sales.order.delete";
            public const string Approve = "sales.order.approve";
            public const string Cancel = "sales.order.cancel";
            public const string Void = "sales.order.void";
            public const string Refund = "sales.order.refund";
        }

        public static class Return
        {
            public const string View = "sales.return.view";
            public const string Create = "sales.return.create";
            public const string Update = "sales.return.update";
            public const string Delete = "sales.return.delete";
            public const string Approve = "sales.return.approve";
            public const string Cancel = "sales.return.cancel";
        }
    }

    public static class Pos
    {
        public static class Order
        {
            public const string View = "pos.order.view";
            public const string Create = "pos.order.create";
            public const string Finalize = "pos.order.finalize";
            public const string Void = "pos.order.void";
            public const string Refund = "pos.order.refund";
            public const string Reprint = "pos.order.reprint";
            public const string Hold = "pos.order.hold";
            public const string Discount = "pos.order.discount";
        }

        public static class Shift
        {
            public const string View = "pos.shift.view";
            public const string Open = "pos.shift.open";
            public const string Close = "pos.shift.close";
            public const string Reconcile = "pos.shift.reconcile";
        }

        public static class Payment
        {
            public const string View = "pos.payment.view";
            public const string Create = "pos.payment.create";
            public const string Refund = "pos.payment.refund";
        }

        public static class Terminal
        {
            public const string View = "pos.terminal.view";
            public const string Create = "pos.terminal.create";
            public const string Update = "pos.terminal.update";
            public const string Delete = "pos.terminal.delete";
        }
    }

    public static class Report
    {
        public static class Sales
        {
            public const string View = "report.sales.view";
            public const string Export = "report.sales.export";
        }

        public static class Inventory
        {
            public const string View = "report.inventory.view";
            public const string Export = "report.inventory.export";
        }

        public static class Profit
        {
            public const string View = "report.profit.view";
            public const string Export = "report.profit.export";
        }

        public static class Purchase
        {
            public const string View = "report.purchase.view";
            public const string Export = "report.purchase.export";
        }

        public static class Shift
        {
            public const string View = "report.shift.view";
            public const string Export = "report.shift.export";
        }

        public static class Receivable
        {
            public const string View = "report.receivable.view";
            public const string Export = "report.receivable.export";
        }

        public static class Payable
        {
            public const string View = "report.payable.view";
            public const string Export = "report.payable.export";
        }
    }

    public static class Security
    {
        public static class User
        {
            public const string View = "security.user.view";
            public const string Create = "security.user.create";
            public const string Update = "security.user.update";
            public const string Delete = "security.user.delete";
            public const string AssignStore = "security.user.assignstore";
            public const string AssignRole = "security.user.assignrole";
        }

        public static class Role
        {
            public const string View = "security.role.view";
            public const string Create = "security.role.create";
            public const string Update = "security.role.update";
            public const string Delete = "security.role.delete";

            /// <summary>
            /// Quyền vào màn gán permission cho role.
            /// Dùng tên ngắn gọn, đồng nhất với UI admin security hiện tại.
            /// </summary>
            public const string Permissions = "security.role.permissions";
            // Alias giữ backward compatibility cho code cũ
            public const string AssignPermission = Permissions;
        }

        public static class Permission
        {
            public const string View = "security.permission.view";
            public const string Manage = "security.permission.manage";
        }

        public static class UserInStore
        {
            public const string View = "security.userinstore.view";
            public const string Create = "security.userinstore.create";
            public const string Update = "security.userinstore.update";
            public const string Delete = "security.userinstore.delete";
        }
    }

    public static class System
    {
        public static class Store
        {
            public const string View = "system.store.view";
            public const string Create = "system.store.create";
            public const string Update = "system.store.update";
            public const string Delete = "system.store.delete";
        }

        public static class Setting
        {
            public const string View = "system.setting.view";
            public const string Update = "system.setting.update";
        }

        public static class AuditLog
        {
            public const string View = "system.auditlog.view";
            public const string Export = "system.auditlog.export";
        }

        public static class Job
        {
            public const string View = "system.job.view";
            public const string Execute = "system.job.execute";
        }

        public static class Integration
        {
            public const string View = "system.integration.view";
            public const string Manage = "system.integration.manage";
        }
    }
}
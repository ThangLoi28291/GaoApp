namespace GaoApp.Application.Common.Security;

/// <summary>Vietnamese presentation only. Permission codes and authorization rules remain unchanged.</summary>
public static class PermissionDisplayNames
{
    private static readonly IReadOnlyDictionary<string, PermissionRecord> Catalog =
        PermissionCatalog.All.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    public static string Group(string? group) => (group ?? "").ToLowerInvariant() switch
    {
        "delivery" => "Giao hàng",
        "admin" => "Tổng quan", "catalog" => "Danh mục & sản phẩm", "customers" => "Chăm sóc khách hàng",
        "inventory" => "Kho hàng", "pos" => "Bán hàng & thu ngân", "purchase" => "Mua & nhập hàng",
        "reports" => "Báo cáo", "security" => "Nhân sự & phân quyền", "system" => "Cấu hình hệ thống",
        "other" or "" => "Chức năng khác", _ => group!
    };

    public static string Feature(string module, string entity) => (module + "." + entity).ToLowerInvariant() switch
    {
        "delivery.order" => "Đơn giao hàng",
        "admin.dashboard" => "Tổng quan cửa hàng",
        "admin.storemonitor" => "Giám sát trực tiếp",
        "catalog.category" => "Danh mục sản phẩm", "catalog.brand" => "Thương hiệu", "catalog.unit" => "Đơn vị tính",
        "catalog.product" => "Sản phẩm", "catalog.productvariant" => "Biến thể sản phẩm",
        "catalog.productattribute" => "Thuộc tính sản phẩm", "catalog.attributevalue" => "Giá trị thuộc tính",
        "catalog.barcode" => "Mã vạch", "catalog.supplier" => "Nhà cung cấp", "catalog.customer" => "Khách hàng",
        "catalog.tax" => "Thuế", "catalog.promotion" => "Khuyến mãi", "catalog.displaypromotion" => "Quảng bá màn hình khách",
        "customer.deposit" => "Đặt cọc khách hàng", "customer.debt" => "Công nợ khách hàng",
        "inventory.warehouse" => "Kho hàng", "inventory.balance" => "Số lượng tồn kho",
        "inventory.transaction" => "Giao dịch tồn kho", "inventory.stockdocument" => "Chứng từ kho",
        "inventory.adjustment" => "Điều chỉnh kho", "inventory.stockcount" => "Kiểm kê",
        "inventory.stocktransfer" => "Chuyển kho", "inventory.issue" => "Xử lý tồn kho âm", "inventory.valuation" => "Giá trị tồn kho",
        "purchase.request" => "Yêu cầu mua hàng", "purchase.order" => "Đơn đặt mua", "purchase.receipt" => "Phiếu nhập hàng",
        "pos.order" => "Đơn bán hàng", "pos.shift" => "Ca thu ngân", "pos.payment" => "Thanh toán", "pos.terminal" => "Máy bán hàng POS",
        "report.sales" => "Báo cáo bán hàng", "report.profit" => "Báo cáo lợi nhuận", "report.inventory" => "Báo cáo tồn kho",
        "report.purchase" => "Báo cáo mua hàng", "report.receivable" => "Công nợ phải thu", "report.payable" => "Công nợ phải trả",
        "report.shift" => "Báo cáo ca thu ngân",
        "report.expenses" => "Chi phí vận hành", "report.treasury" => "Thu chi & đối soát",
        "security.user" => "Tài khoản người dùng", "security.role" => "Vai trò nhân viên",
        "security.permission" => "Danh sách quyền", "security.userinstore" => "Nhân viên trong cửa hàng",
        "system.store" => "Cửa hàng", "system.legalentity" => "Hộ kinh doanh", "system.setting" => "Thiết lập chung",
        "system.auditlog" => "Nhật ký thao tác", "system.job" => "Tác vụ chạy nền", "system.integration" => "Kết nối dịch vụ",
        "system.invoice" => "Hóa đơn điện tử", "system.autoinvoice" => "Hóa đơn tự động", "system.bankaccount" => "Tài khoản ngân hàng",
        "system.receipttemplate" => "Mẫu hóa đơn bán hàng", "system.productlabel" => "In tem sản phẩm",
        _ => "Chức năng khác"
    };

    public static string Permission(string code, string fallback)
    {
        var name = Catalog.TryGetValue(code, out var entry) ? entry.Name : fallback;
        return name.Replace("Multi LegalEntity", "nhiều hộ kinh doanh", StringComparison.OrdinalIgnoreCase)
            .Replace("background jobs", "tác vụ chạy nền", StringComparison.OrdinalIgnoreCase)
            .Replace("audit log", "nhật ký thao tác", StringComparison.OrdinalIgnoreCase)
            .Replace("integration", "kết nối dịch vụ", StringComparison.OrdinalIgnoreCase)
            .Replace("valuation tồn kho", "giá trị tồn kho", StringComparison.OrdinalIgnoreCase)
            .Replace("issue tồn kho", "tồn kho âm", StringComparison.OrdinalIgnoreCase)
            .Replace("terminal POS", "máy bán hàng POS", StringComparison.OrdinalIgnoreCase)
            .Replace("Refund", "Hoàn tiền", StringComparison.OrdinalIgnoreCase)
            .Replace("permission", "quyền", StringComparison.OrdinalIgnoreCase)
            .Replace("voucher", "phiếu ưu đãi", StringComparison.OrdinalIgnoreCase)
            .Replace("role", "vai trò", StringComparison.OrdinalIgnoreCase)
            .Replace("user", "người dùng", StringComparison.OrdinalIgnoreCase)
            .Replace("store", "cửa hàng", StringComparison.OrdinalIgnoreCase)
            .Replace("HKD", "hộ kinh doanh", StringComparison.OrdinalIgnoreCase);
    }
}

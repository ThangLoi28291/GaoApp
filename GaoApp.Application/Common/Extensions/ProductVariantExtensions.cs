using GaoApp.Domain.Entities;

namespace GaoApp.Application.Common.Extensions;

/// <summary>
/// Extension methods dùng chung cho ProductVariant.
/// Mục tiêu:
/// - giữ Entity sạch
/// - gom logic hiển thị về 1 chỗ
/// - các module Inventory / POS / Order / Report dùng chung format
/// </summary>
public static class ProductVariantExtensions
{
    /// <summary>
    /// Trả về tên hiển thị của biến thể sản phẩm.
    ///
    /// Quy tắc phase hiện tại:
    /// - Có Product.Name và có SKU => "Product.Name - SKU"
    /// - Chỉ có Product.Name => "Product.Name"
    /// - Chỉ có SKU => "SKU"
    /// - Không có cả hai => chuỗi rỗng
    ///
    /// Lưu ý:
    /// Method này không truy vấn DB.
    /// Nó chỉ dùng dữ liệu đã được load sẵn trong entity.
    /// Vì vậy khi dùng ở repository/service, cần Include Product nếu muốn lấy Product.Name.
    /// </summary>
    public static string GetDisplayName(this ProductVariant? variant)
    {
        if (variant == null)
            return string.Empty;

        var productName = variant.Product?.Name?.Trim();
        var sku = variant.Sku?.Trim();

        if (!string.IsNullOrWhiteSpace(productName) && !string.IsNullOrWhiteSpace(sku))
            return $"{productName} - {sku}";

        if (!string.IsNullOrWhiteSpace(productName))
            return productName!;

        if (!string.IsNullOrWhiteSpace(sku))
            return sku!;

        return string.Empty;
    }
}
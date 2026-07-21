using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO dùng cho form thêm / sửa barcode của 1 ProductUnitConversion.
/// </summary>
public sealed class ProductVariantUnitBarcodeDto
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "ProductUnitConversionId không hợp lệ.")]
    public int ProductUnitConversionId { get; set; }

    [StringLength(64, ErrorMessage = "Barcode tối đa 64 ký tự.")]
    public string? Barcode { get; set; }

    public BarcodeType BarcodeType { get; set; } = BarcodeType.External;
    // NEW: chỉ dùng để hiển thị UI, không thay đổi enum/DB
    public string BarcodeTypeText { get; set; } = "";

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }

    /// <summary>
    /// Dùng khi UI cần hiển thị thời điểm tạo barcode.
    /// Không ảnh hưởng nghiệp vụ chính.
    /// </summary>
    public DateTime? CreatedAtUtc { get; set; }
}
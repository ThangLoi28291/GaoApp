using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO lưu barcode theo đơn vị.
/// 
/// CHỐT:
/// - Khi thêm mới barcode, có thể không cần nhập Barcode
/// - Server sẽ tự sinh EAN13 nếu là luồng auto-generate
/// </summary>
public class UpsertProductVariantUnitBarcodeRequest
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductUnitConversionId { get; set; }

    /// <summary>
    /// Barcode có thể để null/rỗng ở luồng tạo mới,
    /// vì server sẽ tự sinh.
    /// </summary>
    [StringLength(64)]
    public string? Barcode { get; set; }

    /// <summary>
    /// Barcode nội bộ tự sinh theo EAN13.
    /// </summary>
    public BarcodeType BarcodeType { get; set; } = BarcodeType.Internal;

    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;

    [StringLength(250)]
    public string? Note { get; set; }
}
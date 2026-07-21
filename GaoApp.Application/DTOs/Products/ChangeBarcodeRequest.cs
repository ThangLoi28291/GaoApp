using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

public class ChangeBarcodeRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ProductUnitConversionId không hợp lệ.")]
    public int ProductUnitConversionId { get; set; }

    [Required(ErrorMessage = "Barcode mới không được để trống.")]
    [StringLength(64, ErrorMessage = "Barcode tối đa 64 ký tự.")]
    public string NewBarcode { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
    public string? Reason { get; set; }

    /// <summary>
    /// Giữ lại field này để tương thích request cũ từ UI/API.
    /// Nhưng trong flow governance chuẩn, barcode mới sẽ luôn thành primary.
    /// </summary>
    public bool SetAsPrimary { get; set; } = true;
}
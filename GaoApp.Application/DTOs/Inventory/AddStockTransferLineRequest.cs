using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class AddStockTransferLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Sản phẩm không hợp lệ.")]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Đơn vị không hợp lệ.")]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
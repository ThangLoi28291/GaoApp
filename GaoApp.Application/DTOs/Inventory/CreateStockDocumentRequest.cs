using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class CreateStockDocumentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn HKD nhập hàng.")]
    public int LegalEntityId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho.")]
    public int WarehouseId { get; set; }

    public int? SupplierId { get; set; }

    public DateTime? DocumentDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
    [StringLength(255)]
    public string? DocumentTitle { get; set; }

    public bool HasVat { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do nhập ngoài đơn đặt hàng.")]
    [StringLength(500)]
    public string? DirectReceiptReason { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class UpdateStockDocumentLineRequest
{
    public int? UnitId { get; set; }

    [Range(typeof(decimal), "0.001", "999999999")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Giá chỉ do quản lý chốt ở bước duyệt thương mại. Khi nhân viên chỉ sửa
    /// số lượng, để null để giữ nguyên snapshot giá hiện tại.
    /// </summary>
    [Range(typeof(decimal), "0", "999999999")]
    public decimal? UnitCost { get; set; }

    public int? TaxId { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

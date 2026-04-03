using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class AddStockDocumentLineRequest
{
    [Required]
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Đơn vị nhập. Nếu null thì lấy base unit.
    /// </summary>
    public int? UnitId { get; set; }

    [Range(typeof(decimal), "0.001", "999999999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal UnitCost { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
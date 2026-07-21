using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Tạo mới phiếu kiểm kê.
/// </summary>
public class CreateStockCountDocumentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "WarehouseId không hợp lệ.")]
    public int WarehouseId { get; set; }
    [StringLength(250)]
    public string? DocumentName { get; set; }

    public DateTime? DocumentDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
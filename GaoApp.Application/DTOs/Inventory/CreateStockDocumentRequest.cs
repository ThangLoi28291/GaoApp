using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class CreateStockDocumentRequest
{
    [Required]
    public int WarehouseId { get; set; }

    public int? SupplierId { get; set; }

    public DateTime? DocumentDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class UpdateStockTransferHeaderRequest
{
    [Required]
    public DateTime DocumentDate { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kho nguồn không hợp lệ.")]
    public int FromWarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kho đích không hợp lệ.")]
    public int ToWarehouseId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
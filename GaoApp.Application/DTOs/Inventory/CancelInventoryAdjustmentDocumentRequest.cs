using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class CancelInventoryAdjustmentDocumentRequest
{
    public int Id { get; set; }

    [StringLength(1000)]
    public string? CancelNote { get; set; }
}
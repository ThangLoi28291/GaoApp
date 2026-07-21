using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class AddStockDocumentLineByBarcodeRequest
{
    [Required]
    public string Barcode { get; set; } = default!;

    [Range(typeof(decimal), "0.001", "999999999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal UnitCost { get; set; }

    public int? TaxId { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

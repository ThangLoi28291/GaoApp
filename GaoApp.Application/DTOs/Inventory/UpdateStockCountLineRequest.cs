using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Sửa dòng kiểm kê.
/// </summary>
public class UpdateStockCountLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "UnitId không hợp lệ.")]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "CountedQty không hợp lệ.")]
    public decimal CountedQty { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}
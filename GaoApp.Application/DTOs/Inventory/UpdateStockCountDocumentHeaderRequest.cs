using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Sửa header phiếu kiểm kê.
/// Chỉ áp dụng khi phiếu còn Draft.
/// </summary>
public class UpdateStockCountDocumentHeaderRequest
{
    [Range(1, int.MaxValue)]
    public int StockCountDocumentId { get; set; }
    [StringLength(250)]
    public string? DocumentName { get; set; }

    [Range(1, int.MaxValue)]
    public int? WarehouseId { get; set; }

    public DateTime? DocumentDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}